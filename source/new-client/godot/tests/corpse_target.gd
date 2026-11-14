extends SceneTree
## A dead bot of your realm stays targetable for a resurrection; other bodies don't.
##   Godot --headless --path godot -s res://tests/corpse_target.gd
##
## The server deletes a monster the moment it dies but keeps a bot's body where it fell until it releases, so a dead
## living ServerWorld still holds (by_oid) is a bot. Clicks, select() (the group window and /target end there) and the
## per-tick target check are run against bodies built here: no content, server or zone is needed. A -s script is
## compiled before the autoload names exist, so PlayerData is fetched from the tree and the world scripts are loaded
## only after the autoloads are up.

const MINE := 3                              # PlayerData realm "hib"

var _pd: Node
var _npc: GDScript                           # npc.gd, npc_state.gd (loaded after the autoloads)
var _state: GDScript
var _world: Node3D
var _cam: Camera3D
var _next_oid := 100
var _fails: Array[String] = []


func _initialize() -> void:
	await process_frame
	_pd = root.get_node("PlayerData")
	_pd.character = {"realm": "hib"}
	_npc = load("res://game/npcs/npc.gd")
	_state = load("res://game/npcs/npc_state.gd")
	var script: GDScript = load("res://game/net/server_world.gd")
	if script == null or not script.can_instantiate():
		print("CORPSE_TARGET_FAIL server_world.gd did not compile")
		quit(1)
		return
	_world = script.new()
	_world.process_mode = Node.PROCESS_MODE_DISABLED       # (no zone, viewer or server: nothing ticks on its own)
	root.add_child(_world)
	var me := Node3D.new()
	root.add_child(me)
	_world.player = me
	_cam = Camera3D.new()
	root.add_child(_cam)
	_cam.make_current()
	_cam.look_at_from_position(Vector3(0, 3, 6), Vector3.ZERO)

	var bot := _body(MINE, Vector3.ZERO)
	var monster := _body(0, Vector3(0, 0, -4))
	var enemy := _body(1, Vector3(-4, 0, 0))
	_check_select(bot, monster, enemy)
	_check_dies_while_targeted()
	await _check_clicks(bot, monster, enemy)
	_check_released(bot)
	_check_offline()

	if _fails.is_empty():
		print("CORPSE_TARGET_OK")
		quit(0)
	else:
		for f in _fails:
			print("CORPSE_TARGET_FAIL ", f)
		quit(1)


func _expect(ok: bool, what: String) -> void:
	if not ok:
		_fails.append(what)


## A living the server shows (as ServerWorld._add_npc keeps it), its view lying along +x from `at` once dead:
## a two-bone skeleton, pelvis to head 1.6 m.
func _body(realm: int, at: Vector3, alive := false) -> Node3D:
	var oid := _next_oid
	_next_oid += 1
	var spawn := {"id": "srv%d" % oid, "oid": oid, "name": "Body%d" % oid, "guild": "", "realm": realm, "flags": 0,
			"level": 20, "role": "", "pos": [at.x, at.y, at.z], "heading": 0}
	var st = _state.new(_world.states.size(), spawn, {})
	st.level = 20
	_world.states.append(st)
	_world.by_oid[oid] = st
	var n: Node3D = _npc.new()
	n.state = st
	n.spawn = spawn
	n.manager = _world
	_world.add_child(n)
	n.global_position = at
	var model := Node3D.new()
	var sk := Skeleton3D.new()
	sk.add_bone("Bip01 Pelvis")
	sk.add_bone("Bip01 Head")
	sk.set_bone_parent(1, 0)
	sk.set_bone_pose_position(0, Vector3(0, 0.15, 0))
	sk.set_bone_pose_position(1, Vector3(1.6, 0, 0))
	model.add_child(sk)
	n.add_child(model)
	n.model = model
	st.view = n
	_world.active[st.index] = n
	if not alive:
		_world._set_health(st, 0)
	return n


func _oid(n: Node3D) -> int:
	return int(n.spawn.oid)


func _check_select(bot: Node3D, monster: Node3D, enemy: Node3D) -> void:
	_expect(_world.can_target_corpse(bot), "a dead bot of your realm the server still holds is not a targetable corpse")
	_expect(not _world.can_target_corpse(monster), "a dead monster counted as a targetable corpse")
	_expect(not _world.can_target_corpse(enemy), "a dead enemy-realm bot counted as a targetable corpse")
	_world.clear_target()
	_world.select(bot)
	_expect(_world.target == bot, "select() (click, group window, /target) refused a dead bot of your realm")
	_expect(String(_pd.target.get("name", "")) == String(bot.spawn.name) and is_zero_approx(float(_pd.target.get("health", 1.0))),
			"the target window does not show the dead bot at 0%% health: %s" % str(_pd.target))
	_world.update_views(0)
	_expect(_world.target == bot, "the per-tick check let go of a targeted dead bot")
	_world.select(monster)
	_expect(_world.target == bot, "select() took a dead monster")
	_world.select(enemy)
	_expect(_world.target == bot, "select() took a dead enemy-realm bot")
	_world.clear_target()


func _check_dies_while_targeted() -> void:
	var mate := _body(MINE, Vector3(8, 0, 0), true)
	_world.select(mate)
	_world._set_health(mate.state, 0)
	_world.update_views(0)
	_expect(_world.target == mate, "a bot of your realm that died while targeted lost the target")
	var foe := _body(0, Vector3(-8, 0, 0), true)
	_world.select(foe)
	_world._set_health(foe.state, 0)
	_world.update_views(0)
	_expect(_world.target == null, "a monster that died while targeted kept the target (no target on a corpse)")
	_world._remove(_oid(mate), true, 0)
	_world._remove(_oid(foe), true, 0)


## The click ray at a point of the world (as the zone viewer passes a left click to pick()).
func _click(at: Vector3) -> Node3D:
	return _world.pick(_cam, _cam.unproject_position(at))


func _check_clicks(bot: Node3D, monster: Node3D, enemy: Node3D) -> void:
	_expect(_click(Vector3(1.5, 0.15, 0)) == bot, "a click on a dead bot's body (its head end) did not pick it")
	_expect(_click(Vector3(0.1, 0.15, 0)) == bot, "a click on a dead bot's body (its pelvis) did not pick it")
	_expect(_world.last_pick_dist > 0.0 and _world.last_pick_dist < 10.0,
			"a corpse pick gave no distance along the ray (%f)" % _world.last_pick_dist)
	_expect(_click(Vector3(3.5, 0.15, 0)) == null, "a click 1.6 m past a dead bot's head picked it")
	_expect(_click(Vector3(1.5, 0.15, -4)) == null, "a click on a dead monster's body picked it")
	_expect(_click(Vector3(-3.0, 0.15, 0)) == null, "a click on a dead enemy-realm bot's body picked it")
	# a living in front of the body is clicked instead of it, one behind it is not
	var living := _body(0, Vector3(1.2, 0, 1.2), true)
	var box := StaticBody3D.new()
	box.collision_layer = 1 << 9
	box.collision_mask = 0
	box.set_meta("npc", living)
	box.process_mode = Node.PROCESS_MODE_ALWAYS          # (its world is held still; the click box must be in physics)
	var shape := CollisionShape3D.new()
	shape.shape = BoxShape3D.new()
	(shape.shape as BoxShape3D).size = Vector3(1.0, 2.0, 1.0)
	box.add_child(shape)
	living.add_child(box)
	await physics_frame
	await physics_frame
	_expect(_click(Vector3(1.5, 0.15, 0)) == living, "a living standing in front of a dead bot was not the one clicked")
	living.global_position = Vector3(1.8, 0, -1.2)
	await physics_frame
	await physics_frame
	_expect(_click(Vector3(1.5, 0.15, 0)) == bot, "a living behind a dead bot's body took the click")
	_world._remove(_oid(living), true, 1)


func _check_released(bot: Node3D) -> void:
	_world.select(bot)
	_world._remove(_oid(bot), true, 0)                     # the bot released: the server removes its body (type 0)
	_expect(_world.target == null, "the target stayed on a body the server removed")
	_expect(not _world.can_target_corpse(bot), "a body the server removed is still a targetable corpse")
	_world.select(bot)
	_expect(_world.target == null, "select() took a body the server removed")


## The game's own world (no server) keeps the old client's rule: no target on any corpse.
func _check_offline() -> void:
	var script: GDScript = load("res://game/npcs/npc_manager.gd")
	var offline: Node3D = script.new()
	offline.process_mode = Node.PROCESS_MODE_DISABLED
	root.add_child(offline)
	var st = _state.new(0, {"id": "x", "name": "Hilda", "realm": MINE, "flags": 0, "level": 5, "role": "",
			"pos": [0, 0, 0], "heading": 0}, {})
	st.alive = false
	var n: Node3D = _npc.new()
	n.state = st
	n.spawn = st.spawn
	n.manager = offline
	offline.add_child(n)
	offline.states.append(st)
	offline.active[0] = n
	offline.select(n)
	_expect(offline.target == null, "the game's own world targeted a corpse")

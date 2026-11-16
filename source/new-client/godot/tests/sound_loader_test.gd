extends SceneTree
## Sound files are read off the main thread (game/sound_loader.gd; the 2026-11-16 stutter: a first play read its
## .wav on the main thread, 200-600 ms each on a busy disk).
##   Godot --headless --path godot -s res://tests/sound_loader_test.gd
##
## Checks: want() does not read the file on the calling thread; the callback comes on a later frame with the sound;
## two asks for the same file share one read; a looped copy loops end to end; a callback whose object was freed is
## skipped; a missing file comes back null; once read, want() answers at once.

const Sounds = preload("res://game/sound_loader.gd")
const DIR := "res://content/sounds"
const FILE := "_sound_loader_test.wav"
const MISSING := "_sound_loader_test_missing.wav"

var _fails := 0
var _got: Array = []


class Holder extends Object:
	var called := false

	func take(_st: AudioStream) -> void:
		called = true


func _check(ok: bool, what: String) -> void:
	print("[sound_loader] %s %s" % ["ok" if ok else "FAIL", what])
	if not ok:
		_fails += 1


func _write_wav(path: String) -> void:
	var frames := 4410                                   # 0.1 s of 16-bit mono silence at 44.1 kHz
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_buffer("RIFF".to_ascii_buffer())
	f.store_32(36 + frames * 2)
	f.store_buffer("WAVEfmt ".to_ascii_buffer())
	f.store_32(16)
	f.store_16(1)
	f.store_16(1)
	f.store_32(44100)
	f.store_32(44100 * 2)
	f.store_16(2)
	f.store_16(16)
	f.store_buffer("data".to_ascii_buffer())
	f.store_32(frames * 2)
	var pcm := PackedByteArray()
	pcm.resize(frames * 2)
	f.store_buffer(pcm)
	f.close()


func _initialize() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(DIR))
	var path := ProjectSettings.globalize_path(DIR.path_join(FILE))
	_write_wav(path)
	_run.call_deferred(path)


func _wait(cond: Callable) -> bool:
	for i in 600:                                        # up to ~10 s of frames
		if cond.call():
			return true
		await process_frame
	return cond.call()


func _run(path: String) -> void:
	# 1. not read on the calling thread, answered later, one read for two asks
	Sounds.want(FILE, func(st): _got.append(["a", st]))
	Sounds.want(FILE, func(st): _got.append(["b", st]))
	_check(_got.is_empty(), "want() returns before the file is read")
	_check(not Sounds.has(FILE), "not cached until the worker is done")
	_check(await _wait(func(): return _got.size() == 2), "both callbacks arrive")
	_check(_got.size() == 2 and _got[0][1] is AudioStreamWAV and _got[0][1] == _got[1][1], "both get the same sound")
	_check(Sounds.has(FILE) and Sounds.stream(FILE) is AudioStreamWAV, "cached once read")

	# 2. once read: answered at once
	var now := []
	Sounds.want(FILE, func(st): now.append(st))
	_check(now.size() == 1 and now[0] == Sounds.stream(FILE), "a cached sound is answered at once")

	# 3. a looped copy
	var looped := []
	Sounds.want(FILE, func(st): looped.append(st), true)
	_check(await _wait(func(): return looped.size() == 1), "looped copy arrives")
	var lw: AudioStreamWAV = looped[0] if looped.size() == 1 else null
	_check(lw != null and lw.loop_mode == AudioStreamWAV.LOOP_FORWARD and lw.loop_end == 4410, "looped copy loops end to end")
	_check(lw != Sounds.stream(FILE), "looped and plain copies are separate")

	# 4. a freed object's callback is skipped (a view parked or freed while its file was read)
	var h := Holder.new()
	var other := "_sound_loader_test_2.wav"
	_write_wav(ProjectSettings.globalize_path(DIR.path_join(other)))
	Sounds.want(other, h.take)
	h.free()
	_check(await _wait(func(): return Sounds.has(other)), "read finishes after its asker was freed (no crash)")

	# 5. a missing file: null, and cached as missing
	var missing := []
	Sounds.want(MISSING, func(st): missing.append(st))
	_check(await _wait(func(): return missing.size() == 1), "missing file answers")
	_check(missing.size() == 1 and missing[0] == null, "missing file answers null")

	# 6. blocking mode (probes): read on the calling thread
	Sounds.blocking = true
	var third := "_sound_loader_test_3.wav"
	_write_wav(ProjectSettings.globalize_path(DIR.path_join(third)))
	var sync := []
	Sounds.want(third, func(st): sync.append(st))
	_check(sync.size() == 1 and sync[0] is AudioStreamWAV, "blocking mode answers at once")
	Sounds.blocking = false

	for f in [FILE, other, third]:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(DIR.path_join(f)))
	print("[sound_loader] %s" % ("PASS" if _fails == 0 else "%d FAIL(S)" % _fails))
	quit(_fails)

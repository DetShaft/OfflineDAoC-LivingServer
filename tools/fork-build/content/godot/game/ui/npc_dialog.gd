class_name NpcDialog
extends ClassicWindow
## The NPC interaction window (npctalk.bmp, 384x320): what an NPC says to you, with its [keywords] clickable - the
## client's way of answering an NPC (clicking a bracketed word whispers it back). Trainers, quest givers and other
## talking NPCs use it; the words go to the zone viewer through `keyword`.

signal keyword(npc_id: String, word: String)

## Eden's commander box ends with this hint. The label helper forces capitals, so the window sets the text itself.
const HINT := "Click on HIGHLIGHTED text"

var npc_id := ""
var _title: Label
var _body: RichTextLabel
var _hint: Label
var _compact := false


func build() -> void:
	closable = true
	_title = UiArt.label("", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 20)
	_title.size = Vector2(350, 16)
	add_child(_title)
	_body = RichTextLabel.new()
	_body.position = Vector2(12, 40)
	_body.size = Vector2(352, 245)
	_body.bbcode_enabled = true
	_body.scroll_following = false
	_body.add_theme_font_override("normal_font", UiArt.font(false))
	_body.add_theme_font_size_override("normal_font_size", 10)
	_body.add_theme_color_override("default_color", UiArt.CREAM)
	_body.meta_clicked.connect(func(m): keyword.emit(npc_id, String(m)))
	UiArt.themed(_body)
	add_child(_body)
	_hint = UiArt.label("", 9, UiArt.GOLD_HI, false)
	_hint.text = HINT
	_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hint.visible = false
	add_child(_hint)


## "[word]" becomes a gold link the click handler whispers back.
static func links(text: String) -> String:
	var re := RegEx.create_from_string("\\[([^\\]]+)\\]")
	return re.sub(text.replace("\n", "\n\n"), "[url=$1][color=#f0c060]$1[/color][/url]", true)


## Show an NPC's words. "[word]" becomes a gold link.
## Several things one NPC says at one moment stack (a trainer's greeting and the quests it offers you) instead of the
## last one replacing the rest (owner 2026-10-09: Magus Oreal's quest offer vanished under his training line).
## append keeps the earlier lines (a commander's reply under the menu). compact is the small commander window.
var _frame := -1


func say(id: String, npc_name: String, text: String, append := false, compact := false) -> void:
	var stack := (append or Engine.get_process_frames() == _frame) and id == npc_id and visible
	_frame = Engine.get_process_frames()
	_fit(compact)
	npc_id = id
	_title.text = npc_name.to_upper()
	var bb := links(text)
	_body.scroll_following = stack
	_body.text = (_body.text + "\n\n" + bb) if stack else bb
	show_window()


func _fit(compact: bool) -> void:
	if compact == _compact and (not compact or resizable):
		return
	_compact = compact
	resizable = true
	min_size = Vector2(220, 120)
	set_frame("npctalk")
	set_window_size(Vector2(280, 196) if compact else Vector2(384, 320))
	_title.size = Vector2(size.x - 28, 16)
	var footer := 18 if compact else 0
	_body.size = Vector2(size.x - 24, size.y - 56 - footer)
	_hint.visible = compact
	_hint.position = Vector2(8, size.y - 20)
	_hint.size = Vector2(size.x - 16, 14)

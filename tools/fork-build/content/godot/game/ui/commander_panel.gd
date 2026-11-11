class_name CommanderPanel
extends Control
## Bonedancer orders. The commander's own dialog (clickable [keywords]) is the window;
## this button opens it when the pet is out and you have not targeted it.
## Talks to the server with /commander menu so the same dialog opens from the pet window.

signal command(text: String)

var _root: Control


func build() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root = Control.new()
	_root.size = Vector2(72, 18)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)
	var orders := UiArt.button("ORDERS", Vector2(64, 15), 8)
	orders.position = Vector2(4, 1)
	orders.tooltip_text = "Open your commander's dialog"
	orders.pressed.connect(func(): command.emit("/commander menu"))
	_root.add_child(orders)
	visible = false


func show_for_bonedancer(on: bool) -> void:
	visible = on

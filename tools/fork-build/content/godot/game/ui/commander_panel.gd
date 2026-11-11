class_name CommanderPanel
extends Control
## Bonedancer orders used to have an ORDERS button beside the pet window. Since 1.1.12 the commander's dialog opens
## only when you right-click the commander (ServerWorld.interact -> /commander menu), so the panel shows nothing.
## The script stays because game_ui.gd still creates it (the installer ships game_ui.gd and this file as a pair).

signal command(text: String)


func build() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	visible = false


func show_for_bonedancer(_on: bool) -> void:
	visible = false

extends Node

var movement := Vector2.ZERO
var jump_pressed := false
var _jump_queued := false
# Observation only, outside the rollback property lists.
var recorded_jump_edges := 0

func _ready() -> void:
	NetworkTime.before_tick_loop.connect(_gather_input)

func _exit_tree() -> void:
	NetworkTime.before_tick_loop.disconnect(_gather_input)

func _process(_delta: float) -> void:
	if is_multiplayer_authority() and Input.is_action_just_pressed("jump"):
		_jump_queued = true

func _gather_input() -> void:
	if not is_multiplayer_authority():
		return
	var raw := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	var yaw: float = get_parent().get_node("Presentation/Camera3D").orbit_yaw
	var world := Vector3(raw.x, 0, raw.y).rotated(Vector3.UP, yaw)
	movement = Vector2(world.x, world.z)
	jump_pressed = _jump_queued
	_jump_queued = false
	if jump_pressed:
		recorded_jump_edges += 1

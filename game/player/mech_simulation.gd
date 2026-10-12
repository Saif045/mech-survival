extends Node

const MOVE_SPEED := 6.0
const JUMP_VELOCITY := 5.0
const GRAVITY := 18.0
var grounded := false

func _rollback_tick(delta: float, _tick: int, _is_fresh: bool) -> void:
	var body := get_parent() as CharacterBody3D
	var input := body.get_node("Input")
	var desired: Vector2 = input.movement
	body.velocity.x = desired.x * MOVE_SPEED
	body.velocity.z = desired.y * MOVE_SPEED
	if grounded and input.jump_pressed:
		body.velocity.y = JUMP_VELOCITY
	else:
		body.velocity.y -= GRAVITY * delta
	body.velocity *= NetworkTime.physics_factor
	body.move_and_slide()
	body.velocity /= NetworkTime.physics_factor
	grounded = body.is_on_floor()

extends Node

const JUMP_VELOCITY := 5.0
const GRAVITY := 18.0
var grounded := false

func _rollback_tick(delta: float, _tick: int, _is_fresh: bool) -> void:
	var body := get_parent() as CharacterBody3D
	var input := body.get_node("Input")
	var desired: Vector2 = input.movement
	# Canonical GAS stays outside history. Rollback consumes its current ordinary
	# read-only projection; it never restores or mutates GAS/part state.
	var move_speed: float = body.GasMoveSpeed
	body.velocity.x = desired.x * move_speed
	body.velocity.z = desired.y * move_speed
	if grounded and input.jump_pressed:
		body.velocity.y = JUMP_VELOCITY
	else:
		body.velocity.y -= GRAVITY * delta
	body.velocity *= NetworkTime.physics_factor
	body.move_and_slide()
	body.velocity /= NetworkTime.physics_factor
	grounded = body.is_on_floor()

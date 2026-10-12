extends Node3D

func _ready() -> void:
	top_level = true
	global_position = get_parent().global_position
	var owner_id: int = get_parent().get_node("Input").get_multiplayer_authority()
	var material := StandardMaterial3D.new()
	material.albedo_color = Color.from_hsv(fposmod(owner_id * 0.61803398875, 1.0), 0.65, 0.9)
	for mesh in $Visual.get_children():
		mesh.material_override = material
	NetworkTime.after_tick_loop.connect(_copy_completed_simulation)

func _exit_tree() -> void:
	NetworkTime.after_tick_loop.disconnect(_copy_completed_simulation)

func _copy_completed_simulation() -> void:
	global_position = get_parent().global_position
	var velocity: Vector3 = get_parent().velocity
	if Vector2(velocity.x, velocity.z).length_squared() > 0.01:
		$Visual.rotation.y = atan2(-velocity.x, -velocity.z)

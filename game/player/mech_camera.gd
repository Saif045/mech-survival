extends Camera3D

# Local presentation only; yaw never rotates the authoritative body.
var orbit_yaw := 0.0
var orbit_pitch := -0.23
var distance := 8.0
var _owner := false

func _ready() -> void:
	_owner = get_parent().get_parent().get_node("Input").is_multiplayer_authority()
	current = _owner
	if _owner:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	_update_orbit()

func _unhandled_input(event: InputEvent) -> void:
	if not _owner or Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		return
	if event is InputEventMouseMotion:
		orbit_yaw -= event.relative.x * 0.003
		orbit_pitch = clampf(orbit_pitch - event.relative.y * 0.003, -0.7, 0.12)
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			distance = maxf(6.5, distance - 0.5)
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			distance = minf(9.5, distance + 0.5)
	_update_orbit()

func _update_orbit() -> void:
	rotation = Vector3(orbit_pitch, orbit_yaw, 0)
	position = Vector3(0, 1.3, 0) + basis.z * distance

func _physics_process(_delta: float) -> void:
	if not _owner:
		return
	var target: Vector3 = get_parent().global_position + Vector3(0, 1.3, 0)
	var desired: Vector3 = target + basis.z * distance
	var query := PhysicsRayQueryParameters3D.create(target, desired)
	query.exclude = [get_parent().get_parent().get_rid()]
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	global_position = desired if hit.is_empty() else hit.position + hit.normal * 0.25

func _exit_tree() -> void:
	if _owner:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

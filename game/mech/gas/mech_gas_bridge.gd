extends Node

const MechAttributes = preload("res://game/mech/gas/mech_attribute_set.gd")
const Stats: Array[String] = ["Mobility", "PowerCapacity", "CoolingCapacity"]
const Tags: Array[StringName] = [&"Capability.Mobility", &"Capability.Sensor", &"Capability.Power", &"Capability.Cooling"]

var _authoritative := false
var _asc: AbilitySystemComponent
var _structure: ActiveGameplayEffect
var _development: ActiveGameplayEffect
var _dirty := false
var _mirror_tags: Array[bool] = [false, false, false, false]

func configure_authority(value: bool) -> void:
	_authoritative = value

func _ready() -> void:
	_asc = AbilitySystemComponent.new()
	_asc.name = "AbilitySystemComponent"
	_asc.attribute_sets = [MechAttributes.new()]
	add_child(_asc)
	_asc.set_process(_authoritative)
	_asc.attribute_changed.connect(_attribute_changed)
	_asc.active_effect_added.connect(_effect_changed)
	_asc.active_effect_removed.connect(_effect_changed)
	_asc.tag_added.connect(_tag_changed)
	_asc.tag_removed.connect(_tag_changed)

func _attribute_changed(_attribute: String, _old: float, _new: float, _spec: GameplayEffectSpec) -> void:
	_dirty = true

func _effect_changed(_effect: ActiveGameplayEffect) -> void:
	_dirty = true

func _tag_changed(_tag: StringName) -> void:
	_dirty = true

func consume_changed() -> bool:
	var changed := _dirty
	_dirty = false
	return changed

func update_structure(values: Array, capabilities: Array) -> bool:
	if not _authoritative:
		return false
	# Own exactly one native additive baseline effect; leave unrelated handles intact.
	if _structure != null:
		_asc.remove_active_effect(_structure)
	var effect := GameplayEffect.new()
	effect.policy = GameplayEffect.DurationPolicy.INFINITE
	for i in range(Stats.size()):
		var modifier := GameplayEffectModifier.new()
		modifier.attribute_name = Stats[i]
		modifier.operation = GameplayEffectModifier.Operation.ADD
		modifier.magnitude = float(values[i])
		effect.modifiers.append(modifier)
	for i in range(Tags.size()):
		if bool(capabilities[i]):
			effect.granted_tags.append(Tags[i])
	_structure = _asc.apply_gameplay_effect(effect, _asc)
	return _structure != null

func apply_mirror(values: Array, capabilities: Array) -> bool:
	if _authoritative:
		return false
	var overrides: Dictionary[String, float] = {}
	for i in range(Stats.size()):
		overrides[Stats[i]] = float(values[i])
	_asc.initialize_attribute_overrides(overrides)
	for i in range(Tags.size()):
		var present := bool(capabilities[i])
		if present != _mirror_tags[i]:
			if present:
				_asc.add_tag(Tags[i])
			else:
				_asc.remove_tag(Tags[i])
			_mirror_tags[i] = present
	return true

func capture_snapshot() -> Dictionary:
	return {
		"Mobility": _asc.get_attribute("Mobility").current_value,
		"PowerCapacity": _asc.get_attribute("PowerCapacity").current_value,
		"CoolingCapacity": _asc.get_attribute("CoolingCapacity").current_value,
		"HasMobility": _asc.has_tag_exact(Tags[0]), "HasSensor": _asc.has_tag_exact(Tags[1]),
		"HasPower": _asc.has_tag_exact(Tags[2]), "HasCooling": _asc.has_tag_exact(Tags[3])
	}

func set_development_modifier(effect: GameplayEffect) -> bool:
	if not _authoritative:
		return false
	if effect == null:
		if _development == null:
			return false
		_asc.remove_active_effect(_development)
		_development = null
		return true
	if _development != null:
		return false
	_development = _asc.apply_gameplay_effect(effect, _asc)
	return _development != null

func has_development_modifier() -> bool:
	return _asc.has_tag_exact(&"Development.MechModifier")

func uses_real_asc() -> bool:
	return _asc is AbilitySystemComponent and _asc.get_script().resource_path == "res://addons/GodotGAS/components/ability_system_component.gd" and _asc.has_attribute("Mobility") and not _asc.has_attribute("Health") and not _asc.has_attribute("Stamina")

func shutdown() -> bool:
	if _development != null:
		_asc.remove_active_effect(_development)
		_development = null
	if _structure != null:
		_asc.remove_active_effect(_structure)
		_structure = null
	for i in range(Tags.size()):
		if _mirror_tags[i]:
			_asc.remove_tag(Tags[i])
			_mirror_tags[i] = false
	_asc.initialize_attribute_overrides({"Mobility": 0.0, "PowerCapacity": 0.0, "CoolingCapacity": 0.0})
	return not _asc.has_tag_exact(&"Development.MechModifier") and not _asc.has_tag_exact(Tags[0]) and not _asc.has_tag_exact(Tags[1]) and not _asc.has_tag_exact(Tags[2]) and not _asc.has_tag_exact(Tags[3]) and _asc.get_attribute("Mobility").current_value == 0.0 and _asc.get_attribute("PowerCapacity").current_value == 0.0 and _asc.get_attribute("CoolingCapacity").current_value == 0.0

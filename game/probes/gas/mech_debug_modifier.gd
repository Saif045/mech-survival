extends GameplayEffect

# Acceptance/probe-only reversible native modifier, never a game mechanic.
func _init() -> void:
	policy = DurationPolicy.INFINITE
	granted_tags = [&"Development.MechModifier"]
	var modifier := GameplayEffectModifier.new()
	modifier.attribute_name = "Mobility"
	modifier.operation = GameplayEffectModifier.Operation.ADD
	modifier.magnitude = 0.5
	modifiers = [modifier]

extends AttributeSet

# No player attributes and no gameplay clamping/threshold policy.
# Replacing one additive structural effect must not clamp intermediate deltas.
var Mobility: AttributeData = AttributeData.new(0.0)
var PowerCapacity: AttributeData = AttributeData.new(0.0)
var CoolingCapacity: AttributeData = AttributeData.new(0.0)
var Energy: AttributeData = AttributeData.new(0.0)
var Heat: AttributeData = AttributeData.new(0.0)

func pre_attribute_change(attribute_name: String, proposed_value: float) -> float:
	if attribute_name == "Energy":
		return maxf(0.0, proposed_value)
	if attribute_name == "Heat":
		return clampf(proposed_value, 0.0, 100.0)
	return proposed_value

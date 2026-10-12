extends AttributeSet

# No player attributes and no gameplay clamping/threshold policy.
# Replacing one additive structural effect must not clamp intermediate deltas.
var Mobility: AttributeData = AttributeData.new(0.0)
var PowerCapacity: AttributeData = AttributeData.new(0.0)
var CoolingCapacity: AttributeData = AttributeData.new(0.0)

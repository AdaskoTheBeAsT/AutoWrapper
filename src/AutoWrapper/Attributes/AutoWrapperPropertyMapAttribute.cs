using System;

namespace AutoWrapper.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AutoWrapperPropertyMapAttribute
    : Attribute
{
    public AutoWrapperPropertyMapAttribute()
    {
    }

    /// <summary>
    /// Gets or sets the name of the property.
    /// </summary>
    /// <value>The name of the property.</value>
    public AutoWrapperPropertyMapAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; set; } = string.Empty;
}

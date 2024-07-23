using System;

namespace AutoWrapper.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequestDataLogIgnoreAttribute
    : Attribute;

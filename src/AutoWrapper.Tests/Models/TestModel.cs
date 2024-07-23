using System;

namespace AutoWrapper.Tests.Models;

public class TestModel
{
    public enum StatusType
    {
        Unknown,

        Active,

        InActive,
    }

    public Guid Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public DateTime DateOfBirth { get; set; }

    public StatusType Type { get; set; }
}

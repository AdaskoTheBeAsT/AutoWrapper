namespace AutoWrapper.Tests.Models;

public class Error
{
    public Error(
        string message,
        string code,
        InnerError inner)
    {
        Message = message;
        Code = code;
        InnerError = inner;
    }

    public string Message { get; set; }

    public string Code { get; set; }

    public InnerError InnerError { get; set; }
}

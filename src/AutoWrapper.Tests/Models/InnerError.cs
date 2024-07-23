namespace AutoWrapper.Tests.Models;

public class InnerError
{
    public InnerError(
        string reqId,
        string reqDate)
    {
        RequestId = reqId;
        Date = reqDate;
    }

    public string RequestId { get; set; }

    public string Date { get; set; }
}

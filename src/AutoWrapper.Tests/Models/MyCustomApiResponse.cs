using System;

namespace AutoWrapper.Tests.Models;

public class MyCustomApiResponse
{
    public MyCustomApiResponse(
        DateTime sentDate,
        object? payload = null,
        string message = "",
        int statusCode = 200,
        Pagination? pagination = null)
    {
        Code = statusCode;
        Message = message == string.Empty ? "Success" : message;
        Payload = payload;
        SentDate = sentDate;
        Pagination = pagination;
    }

    public MyCustomApiResponse(
        DateTime sentDate,
        object? payload = null,
        Pagination? pagination = null)
    {
        Code = 200;
        Message = "Success";
        Payload = payload;
        SentDate = sentDate;
        Pagination = pagination;
    }

    public MyCustomApiResponse(object payload)
    {
        Code = 200;
        Message = "Success";
        Payload = payload;
    }

    public int Code { get; set; }

    public string Message { get; set; }

    public object? Payload { get; set; }

    public DateTime SentDate { get; set; }

    public Pagination? Pagination { get; set; }
}

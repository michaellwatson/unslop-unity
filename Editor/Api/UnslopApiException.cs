using System;

namespace Unslop.UnityBridge.Editor.Api
{
    public sealed class UnslopApiException : Exception
    {
        public int StatusCode { get; }
        public string CorrelationId { get; }
        public string ResponseBody { get; }
        public bool IsUnauthorized => StatusCode == 401 || StatusCode == 403;
        public bool IsPreconditionFailed => StatusCode == 412;
        public bool IsIdempotencyConflict => StatusCode == 409;

        public UnslopApiException(int statusCode, string message, string correlationId, string responseBody)
            : base(message)
        {
            StatusCode = statusCode;
            CorrelationId = correlationId;
            ResponseBody = responseBody;
        }
    }
}

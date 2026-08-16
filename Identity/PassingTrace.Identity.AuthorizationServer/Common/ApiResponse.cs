namespace PassingTrace.Identity.AuthorizationServer.Common
{
    /// <summary>
    /// API统一响应结构。
    /// </summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    public sealed class ApiResponse<T>
    {
        private ApiResponse(
            bool success,
            string code,
            string message,
            T? data,
            string traceId)
        {
            Success = success;
            Code = code;
            Message = message;
            Data = data;
            TraceId = traceId;
        }

        /// <summary>
        /// 请求是否成功。
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// 业务响应码；00000表示成功，A开头表示客户端错误，B开头表示系统错误。
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// 面向调用方的简要提示信息。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 业务响应数据；失败时通常为空。
        /// </summary>
        public T? Data { get; }

        /// <summary>
        /// 请求跟踪标识，可用于定位服务端日志。
        /// </summary>
        public string TraceId { get; }

        public static ApiResponse<T> Ok(
            T data,
            string traceId,
            string message = "操作成功") =>
            new(true, ApiResponseCodes.Success, message, data, traceId);

        public static ApiResponse<T> Fail(
            string code,
            string message,
            string traceId) =>
            new(false, code, message, default, traceId);
    }

    public static class ApiResponseCodes
    {
        public const string Success = "00000";
        public const string ValidationFailed = "A0400";
        public const string Unauthorized = "A0401";
        public const string Forbidden = "A0403";
        public const string UserNotFound = "A0404";
        public const string UserAlreadyExists = "A0409";
        public const string ResourceNotFound = "A1404";
        public const string Conflict = "A1409";
        public const string InsufficientBalance = "A1420";
        public const string UnsupportedCurrency = "A1421";
        public const string ProductUnavailable = "A1422";
        public const string InternalError = "B0001";
        public const string ServiceUnavailable = "B0002";
    }

}

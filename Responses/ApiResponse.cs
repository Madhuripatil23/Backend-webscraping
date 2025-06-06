namespace webscrapperapi.Responses
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int ErrorCode { get; set; }
        public T? Data { get; set; }

        public static ApiResponse<T> SuccessResponse(T data, string message = "Request successful")
            => new() { Success = true, Message = message, ErrorCode = 0, Data = data };

        public static ApiResponse<T> ErrorResponse(string message, int errorCode)
            => new() { Success = false, Message = message, ErrorCode = errorCode, Data = default };
    }
}

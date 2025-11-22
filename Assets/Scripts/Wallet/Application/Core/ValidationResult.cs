using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Generic validation/result envelope for service operations.
    /// </summary>
    public class ValidationResult
    {
        protected ValidationResult(bool success, string error, string message)
        {
            Success = success;
            Error = error ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
        public string Message { get; }

        public static ValidationResult Ok(string message = null)
        {
            return new ValidationResult(true, null, message);
        }

        public static ValidationResult Fail(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "Unknown error.";
            }

            return new ValidationResult(false, error, null);
        }
    }

    public sealed class ValidationResult<T> : ValidationResult
    {
        private ValidationResult(bool success, string error, string message, T data)
            : base(success, error, message)
        {
            Data = data;
        }

        public T Data { get; }

        public static ValidationResult<T> Ok(T data, string message = null)
        {
            return new ValidationResult<T>(true, null, message, data);
        }

        public static ValidationResult<T> Fail(string error)
        {
            var fail = ValidationResult.Fail(error);
            return new ValidationResult<T>(false, fail.Error, fail.Message, default);
        }
    }

    public sealed class ValidationResult<T1, T2> : ValidationResult
    {
        private ValidationResult(bool success, string error, string message, T1 data1, T2 data2)
            : base(success, error, message)
        {
            Data1 = data1;
            Data2 = data2;
        }

        public T1 Data1 { get; }
        public T2 Data2 { get; }

        public static ValidationResult<T1, T2> Ok(T1 data1, T2 data2, string message = null)
        {
            return new ValidationResult<T1, T2>(true, null, message, data1, data2);
        }

        public static ValidationResult<T1, T2> Fail(string error)
        {
            var fail = ValidationResult.Fail(error);
            return new ValidationResult<T1, T2>(false, fail.Error, fail.Message, default, default);
        }
    }
}

namespace ECommerce.Domain.Common
{
    public class Result
    {
        protected internal Result(bool isSuccess, Error? error)
        {
            if (isSuccess && error is not null)
                throw new InvalidOperationException("A result cannot be successful and contain an error.");

            if (!isSuccess && error is null)
                throw new InvalidOperationException("A failure result must contain an error.");

            IsSuccess = isSuccess;
            Error = error;
        }

        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public Error? Error { get; }

        public static Result Success() => new(true, null);
        public static Result Failure(Error error) => new(false, error);

        public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);
        public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

        public static implicit operator Result(Error error) => Failure(error);

        public TResult Match<TResult>(
            Func<TResult> onSuccess,
            Func<Error, TResult> onFailure)
        {
            return IsSuccess ? onSuccess() : onFailure(Error!);
        }

        public void Match(
            Action onSuccess,
            Action<Error> onFailure)
        {
            if (IsSuccess)
            {
                onSuccess();
            }
            else
            {
                onFailure(Error!);
            }
        }
    }

    public class Result<TValue> : Result
    {
        private readonly TValue? _value;

        protected internal Result(TValue? value, bool isSuccess, Error? error)
            : base(isSuccess, error)
        {
            _value = value;
        }

        public TValue Value => IsSuccess
            ? _value!
            : throw new InvalidOperationException("The value of a failure result cannot be accessed.");

        // إمكانية التحويل الضمني (Implicit Operator) لتسهيل الإرجاع
        public static implicit operator Result<TValue>(TValue? value) =>
            value is not null ? Success(value) : Failure<TValue>(Error.NotFound("Result.NullValue", "The specified value is null."));

        public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

        public TResult Match<TResult>(
            Func<TValue, TResult> onSuccess,
            Func<Error, TResult> onFailure)
        {
            return IsSuccess ? onSuccess(Value) : onFailure(Error!);
        }

        public void Match(
            Action<TValue> onSuccess,
            Action<Error> onFailure)
        {
            if (IsSuccess)
            {
                onSuccess(Value);
            }
            else
            {
                onFailure(Error!);
            }
        }
    }
}

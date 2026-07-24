namespace Ncv.Core;

/// <summary>
/// 파싱 결과. 실패 시 예외 대신 행번호+사유를 담아 반환한다.
/// </summary>
public sealed class ParseResult<T>
{
    public bool Success { get; }
    public T? Value { get; }

    /// <summary>실패가 발생한 입력 행 번호 (1-base). 행과 무관한 실패는 0.</summary>
    public int LineNumber { get; }

    public string? Error { get; }

    private ParseResult(bool success, T? value, int lineNumber, string? error)
    {
        Success = success;
        Value = value;
        LineNumber = lineNumber;
        Error = error;
    }

    public static ParseResult<T> Ok(T value) => new(true, value, 0, null);

    public static ParseResult<T> Fail(int lineNumber, string error) => new(false, default, lineNumber, error);

    /// <summary>실패 결과를 다른 값 타입으로 전파한다.</summary>
    public ParseResult<TOther> As<TOther>()
    {
        if (Success)
            throw new InvalidOperationException("성공 결과는 타입 전파할 수 없습니다.");
        return ParseResult<TOther>.Fail(LineNumber, Error!);
    }

    public override string ToString() =>
        Success ? "OK" : $"행 {LineNumber}: {Error}";
}

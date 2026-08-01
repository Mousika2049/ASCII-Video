using System;

namespace AsciiFlow.Core.Video;

/// <summary>
/// FFmpeg 解码器异常类
/// </summary>
public class FFmpegDecoderException : Exception
{
    /// <summary>
    /// FFmpeg 错误码
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// 错误类型
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public FFmpegDecoderException(string message) : base(message)
    {
        ErrorType = "General";
    }

    /// <summary>
    /// 构造函数（带错误码）
    /// </summary>
    public FFmpegDecoderException(string message, int errorCode) : base(message)
    {
        ErrorCode = errorCode;
        ErrorType = MapErrorType(errorCode);
    }

    /// <summary>
    /// 构造函数（带内部异常）
    /// </summary>
    public FFmpegDecoderException(string message, Exception innerException) : base(message, innerException)
    {
        ErrorType = "General";
    }

    /// <summary>
    /// 构造函数（完整）
    /// </summary>
    public FFmpegDecoderException(string message, int errorCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorType = MapErrorType(errorCode);
    }

    /// <summary>
    /// 根据错误码映射错误类型
    /// </summary>
    private static string MapErrorType(int errorCode)
    {
        return errorCode switch
        {
            -1 => "InvalidData",       // AVERROR(EINVAL)
            -2 => "NotFound",          // AVERROR(ENOENT)
            -5 => "IOError",           // AVERROR(EIO)
            -11 => "EAGAIN",           // AVERROR(EAGAIN)
            -22 => "InvalidArgument",  // AVERROR(EINVAL)
            _ => "Unknown"
        };
    }

    /// <summary>
    /// 重写 ToString 方法
    /// </summary>
    public override string ToString()
    {
        var result = base.ToString();
        if (ErrorCode != 0)
        {
            result += $"\nFFmpeg Error Code: {ErrorCode}";
        }
        if (!string.IsNullOrEmpty(ErrorType))
        {
            result += $"\nError Type: {ErrorType}";
        }
        return result;
    }
}

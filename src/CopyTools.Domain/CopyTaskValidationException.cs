namespace CopyTools.Domain;

public sealed class CopyTaskValidationException : Exception
{
    public CopyTaskValidationException(string message)
        : base(message)
    {
    }
}

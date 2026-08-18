using System;

public class ThrowInFilter
{
    public void Ternary(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle ? true : throw new InvalidOperationException()) // Noncompliant {{Remove this throw from the exception filter; the CLR silently treats the filter as false when it throws.}}
        {
        }
    }

    public void NullCoalescing(bool? canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle ?? throw new InvalidOperationException()) // Noncompliant
        {
        }
    }

    public void SwitchExpression(int code)
    {
        try
        {
            Work();
        }
        catch (Exception) when (code switch { 0 => true, _ => throw new InvalidOperationException() }) // Noncompliant
        {
        }
    }

    public void NestedInArgument(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (Evaluate(canHandle ? true : throw new InvalidOperationException())) // Noncompliant
        {
        }
    }

    public void TwoThrowsInOneFilter(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (Evaluate(canHandle ? true : throw new InvalidOperationException())    // Noncompliant
                                && Evaluate(canHandle ? true : throw new NotSupportedException()))   // Noncompliant
        {
        }
    }

    public void Parenthesized(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when ((canHandle ? true : throw new InvalidOperationException())) // Noncompliant
        {
        }
    }

    public void FilterOfSpecificExceptionType(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (InvalidOperationException ex) when (canHandle ? ex.Message.Length > 0 : throw new NotSupportedException()) // Noncompliant
        {
        }
    }

    public void InnerCatchInsideOuterCatchBody(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception)
        {
            try
            {
                Work();
            }
            catch (Exception) when (canHandle ? true : throw new InvalidOperationException()) // Noncompliant
            {
            }
        }
    }

    // The throw sits inside a filter that is itself inside a lambda: the enclosing lambda is outside the filter and
    // must not make the filter look safe.
    public void FilterInsideLambda(bool canHandle)
    {
        Action run = () =>
        {
            try
            {
                Work();
            }
            catch (Exception) when (canHandle ? true : throw new InvalidOperationException()) // Noncompliant
            {
            }
        };
        run();
    }

    public void CompliantLambdaInFilter(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (Evaluate(() => throw new InvalidOperationException())) // Compliant - the filter itself never throws, the delegate is only passed along
        {
        }
    }

    public void CompliantAnonymousMethodInFilter(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (Evaluate(delegate { return canHandle ? true : throw new InvalidOperationException(); })) // Compliant
        {
        }
    }

    public void CompliantNestedLambdaInFilter()
    {
        try
        {
            Work();
        }
        catch (Exception) when (Evaluate(() => Evaluate(() => throw new InvalidOperationException()))) // Compliant
        {
        }
    }

    public void CompliantThrowStatementInCatchBody(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle)
        {
            throw new InvalidOperationException(); // Compliant - a throw from the handler body is the normal way to give up
        }
    }

    public void CompliantThrowExpressionInCatchBody(bool canHandle, string value)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle)
        {
            _ = value ?? throw new InvalidOperationException(); // Compliant
        }
    }

    public void CompliantThrowExpressionInTryBlock(string value)
    {
        try
        {
            _ = value ?? throw new InvalidOperationException(); // Compliant
        }
        catch (Exception)
        {
        }
    }

    public void CompliantLambdaInCatchBody(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle)
        {
            Func<bool> throwing = () => throw new InvalidOperationException(); // Compliant
            _ = throwing;
        }
    }

    public string CompliantThrowExpressionWithoutTry(string value) =>
        value ?? throw new InvalidOperationException(); // Compliant

    public void CompliantFilterWithoutThrow(bool canHandle)
    {
        try
        {
            Work();
        }
        catch (Exception) when (canHandle) // Compliant
        {
        }
    }

    private static bool Evaluate(bool condition) => condition;
    private static bool Evaluate(Func<bool> condition) => condition();
    private static void Work() { }
}

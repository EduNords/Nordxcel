using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Devolve o valor na posição <c>índice</c> (base 1) entre os argumentos
/// seguintes. <c>ESCOLHER(2;"A";"B";"C")</c> devolve <c>"B"</c>.
/// <para>
/// Como <c>ARRED</c>, o índice é truncado, não arredondado — <c>2,9</c> aponta
/// para a segunda opção, não a terceira.
/// </para>
/// </summary>
public sealed class ChooseFunction() : FormulaFunction("ESCOLHER", 2, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double rawIndex, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        int index = (int)Math.Truncate(rawIndex);
        int valueCount = call.ArgumentCount - 1;

        if (index < 1 || index > valueCount)
        {
            return CellValue.Error(CellErrorType.Value);
        }

        return call.GetScalar(index);
    }
}

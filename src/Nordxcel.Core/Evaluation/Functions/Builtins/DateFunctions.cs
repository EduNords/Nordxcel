using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Data de hoje, como serial do Excel. "Volátil" só no sentido de que
/// recalcula sempre que a célula é reavaliada — não fica atualizando sozinha
/// em tempo real, igual ao próprio Excel, que também só atualiza ao reabrir o
/// arquivo ou forçar um recálculo.
/// </summary>
public sealed class TodayFunction() : FormulaFunction("HOJE", 0, 0)
{
    public override CellValue Invoke(FunctionCall call) =>
        CellValue.Number(ExcelDate.ToSerial(DateTime.Today));
}

/// <summary>
/// Fração de ano entre duas datas, na convenção pedida por <c>base</c>.
/// <para>
/// Cobre as três convenções com fórmula exata e sem ambiguidade: <c>0</c> (30/360
/// americana, a convenção padrão do Excel), <c>2</c> (dias corridos/360),
/// <c>3</c> (dias corridos/365) e <c>4</c> (30/360 europeia). A convenção
/// <c>1</c> (dias corridos/dias reais do ano) fica de fora de propósito — o
/// algoritmo real do Excel para ela lida com períodos parciais de um jeito que
/// não tem fórmula fechada simples, e uma aproximação errada é pior que não
/// suportar: vira #NÚM!, não um número sutilmente errado.
/// </para>
/// </summary>
public sealed class YearFracFunction() : FormulaFunction("FRAÇÃOANO", 2, 3)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double startSerial, out CellErrorType error) ||
            !FunctionArguments.TryGetNumber(call, 1, out double endSerial, out error))
        {
            return CellValue.Error(error);
        }

        int basis = 0;

        if (call.ArgumentCount > 2 && !call.IsMissing(2))
        {
            if (!FunctionArguments.TryGetNumber(call, 2, out double rawBasis, out error))
            {
                return CellValue.Error(error);
            }

            basis = (int)Math.Truncate(rawBasis);
        }

        if (!ExcelDate.TryFromSerial(startSerial, out DateTime start) ||
            !ExcelDate.TryFromSerial(endSerial, out DateTime end))
        {
            return CellValue.Error(CellErrorType.Number);
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        double? fraction = basis switch
        {
            0 => Us30360Days(start, end) / 360d,
            2 => (end - start).TotalDays / 360d,
            3 => (end - start).TotalDays / 365d,
            4 => Eu30360Days(start, end) / 360d,
            _ => null,
        };

        return fraction is { } value
            ? NumericResult.FromDouble(value)
            : CellValue.Error(CellErrorType.Number);
    }

    /// <summary>30/360 americana: mês sempre com 30 dias, com o ajuste de fim de mês do Excel.</summary>
    private static double Us30360Days(DateTime start, DateTime end)
    {
        int startDay = Math.Min(start.Day, 30);
        int endDay = end.Day;

        if (startDay == 30 && endDay == 31)
        {
            endDay = 30;
        }

        return (end.Year - start.Year) * 360d + (end.Month - start.Month) * 30d + (endDay - startDay);
    }

    /// <summary>30/360 europeia: dia 31 sempre vira 30, nos dois extremos, sem depender um do outro.</summary>
    private static double Eu30360Days(DateTime start, DateTime end)
    {
        int startDay = start.Day == 31 ? 30 : start.Day;
        int endDay = end.Day == 31 ? 30 : end.Day;

        return (end.Year - start.Year) * 360d + (end.Month - start.Month) * 30d + (endDay - startDay);
    }
}

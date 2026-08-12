using Nordxcel.Core.Model;

namespace Nordxcel.Core.Layout;

/// <summary>
/// O que está selecionado numa aba: a célula ativa e até onde a seleção se estende.
/// <para>
/// A célula ativa é a que recebe o que for digitado e <b>não se move</b> quando a
/// seleção cresce com Shift. É por isso que existem dois pontos em vez de um
/// retângulo: selecionar B2:D10 com Shift mantém a digitação indo para B2.
/// </para>
/// </summary>
public sealed class SheetSelection
{
    /// <summary>Célula que recebe a digitação. É também a âncora da extensão.</summary>
    public CellAddress Active { get; private set; } = CellAddress.Origin;

    /// <summary>Canto oposto da seleção.</summary>
    public CellAddress Focus { get; private set; } = CellAddress.Origin;

    /// <summary>Retângulo selecionado, já normalizado.</summary>
    public CellRange Range => new(Active, Focus);

    public bool IsSingleCell => Active.Equals(Focus);

    /// <summary>Verdadeiro quando a seleção pega colunas inteiras.</summary>
    public bool IsWholeColumns => Range.Start.Row == 0 && Range.End.Row == CellAddress.MaxRows - 1;

    /// <summary>Verdadeiro quando a seleção pega linhas inteiras.</summary>
    public bool IsWholeRows => Range.Start.Column == 0 && Range.End.Column == CellAddress.MaxColumns - 1;

    public bool Contains(CellAddress address) => Range.Contains(address);

    /// <summary>Move a seleção para uma única célula.</summary>
    public void MoveTo(CellAddress address)
    {
        Active = address;
        Focus = address;
    }

    /// <summary>Estende a seleção até a célula, mantendo a ativa onde está.</summary>
    public void ExtendTo(CellAddress address) => Focus = address;

    public void SelectColumns(int fromColumn, int toColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromColumn);
        ArgumentOutOfRangeException.ThrowIfNegative(toColumn);

        Active = new CellAddress(0, fromColumn);
        Focus = new CellAddress(CellAddress.MaxRows - 1, toColumn);
    }

    public void SelectRows(int fromRow, int toRow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromRow);
        ArgumentOutOfRangeException.ThrowIfNegative(toRow);

        Active = new CellAddress(fromRow, 0);
        Focus = new CellAddress(toRow, CellAddress.MaxColumns - 1);
    }

    public void SelectAll()
    {
        Active = CellAddress.Origin;
        Focus = new CellAddress(CellAddress.MaxRows - 1, CellAddress.MaxColumns - 1);
    }

    /// <summary>Texto da caixa de nome, como o Excel mostra: <c>B7</c> ou <c>B2:D10</c>.</summary>
    public string ToReferenceText() => IsSingleCell ? Active.ToString() : Range.ToString();
}

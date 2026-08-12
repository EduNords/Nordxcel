using Nordxcel.Core.Model;

namespace Nordxcel.Desktop.Controls;

public enum SpreadsheetHitKind
{
    Outside,
    Cell,
    ColumnHeader,
    RowHeader,
    Corner,
}

/// <summary>O que existe embaixo de um ponto da tela.</summary>
public readonly record struct SpreadsheetHit(SpreadsheetHitKind Kind, CellAddress Address)
{
    public static readonly SpreadsheetHit Outside = new(SpreadsheetHitKind.Outside, CellAddress.Origin);

    public bool IsCell => Kind is SpreadsheetHitKind.Cell;
}

using Nordxcel.Core.Model;

namespace Nordxcel.Core.Editing;

/// <summary>
/// Uma célula antes e depois de uma mudança. É a unidade que desfazer/refazer
/// manipula: aplicar <see cref="Before"/> desfaz, aplicar <see cref="After"/> refaz.
/// </summary>
public readonly record struct CellEdit(CellLocation Location, Cell Before, Cell After);

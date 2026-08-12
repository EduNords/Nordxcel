using System.Globalization;

namespace Nordxcel.Core.Model;

/// <summary>Categoria do conteúdo de uma célula, seguindo a tipagem do Excel.</summary>
public enum CellValueKind
{
    /// <summary>Célula vazia. Vale zero em contexto numérico e texto vazio em contexto textual.</summary>
    Blank = 0,

    Number,
    Text,
    Logical,
    Error,
}

/// <summary>
/// Valor de uma célula: vazio, número, texto, lógico ou erro.
/// É um <c>struct</c> porque um modelo DCF grande carrega dezenas de milhares
/// desses valores e alocar um objeto por célula sairia caro no recálculo.
/// </summary>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly double _number;
    private readonly string? _text;
    private readonly CellErrorType _error;

    private CellValue(CellValueKind kind, double number, string? text, CellErrorType error)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _error = error;
    }

    /// <summary>Categoria do valor. O <c>default</c> do struct é <see cref="CellValueKind.Blank"/>.</summary>
    public CellValueKind Kind { get; }

    /// <summary>Célula vazia.</summary>
    public static CellValue Blank => default;

    /// <summary>Constante lógica verdadeira.</summary>
    public static CellValue True { get; } = Logical(true);

    /// <summary>Constante lógica falsa.</summary>
    public static CellValue False { get; } = Logical(false);

    public static CellValue Number(double value) =>
        new(CellValueKind.Number, value, null, CellErrorType.None);

    public static CellValue Text(string value) =>
        new(CellValueKind.Text, 0, value ?? throw new ArgumentNullException(nameof(value)), CellErrorType.None);

    public static CellValue Logical(bool value) =>
        new(CellValueKind.Logical, value ? 1 : 0, null, CellErrorType.None);

    public static CellValue Error(CellErrorType error) =>
        error is CellErrorType.None
            ? throw new ArgumentOutOfRangeException(nameof(error), "CellErrorType.None não é um erro válido.")
            : new CellValue(CellValueKind.Error, 0, null, error);

    public bool IsBlank => Kind is CellValueKind.Blank;

    public bool IsNumber => Kind is CellValueKind.Number;

    public bool IsText => Kind is CellValueKind.Text;

    public bool IsLogical => Kind is CellValueKind.Logical;

    public bool IsError => Kind is CellValueKind.Error;

    /// <summary>
    /// Número contido na célula. Lógicos contam como 1/0 e vazias como 0, igual ao Excel.
    /// Texto e erro lançam — o avaliador deve tratá-los antes de chegar aqui.
    /// </summary>
    public double AsNumber() => Kind switch
    {
        CellValueKind.Number or CellValueKind.Logical => _number,
        CellValueKind.Blank => 0d,
        _ => throw new InvalidOperationException($"Valor do tipo {Kind} não é numérico."),
    };

    /// <summary>Texto contido na célula. Vazia devolve string vazia.</summary>
    public string AsText() => Kind switch
    {
        CellValueKind.Text => _text!,
        CellValueKind.Blank => string.Empty,
        _ => throw new InvalidOperationException($"Valor do tipo {Kind} não é texto."),
    };

    /// <summary>Valor lógico. Números seguem a regra do Excel: zero é falso, qualquer outro é verdadeiro.</summary>
    public bool AsLogical() => Kind switch
    {
        CellValueKind.Logical or CellValueKind.Number => _number != 0d,
        CellValueKind.Blank => false,
        _ => throw new InvalidOperationException($"Valor do tipo {Kind} não é lógico."),
    };

    /// <summary>Erro contido na célula.</summary>
    public CellErrorType AsError() =>
        IsError ? _error : throw new InvalidOperationException($"Valor do tipo {Kind} não é um erro.");

    /// <summary>
    /// Converte o valor para número quando isso for possível sem ambiguidade.
    /// Devolve <c>false</c> para texto e erro, que o chamador precisa tratar caso a caso.
    /// </summary>
    public bool TryGetNumber(out double number)
    {
        switch (Kind)
        {
            case CellValueKind.Number:
            case CellValueKind.Logical:
                number = _number;
                return true;
            case CellValueKind.Blank:
                number = 0d;
                return true;
            default:
                number = 0d;
                return false;
        }
    }

    public bool Equals(CellValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            CellValueKind.Blank => true,
            CellValueKind.Number or CellValueKind.Logical => _number.Equals(other._number),
            CellValueKind.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
            CellValueKind.Error => _error == other._error,
            _ => false,
        };
    }

    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        CellValueKind.Blank => 0,
        CellValueKind.Number or CellValueKind.Logical => HashCode.Combine(Kind, _number),
        CellValueKind.Text => HashCode.Combine(Kind, _text),
        CellValueKind.Error => HashCode.Combine(Kind, _error),
        _ => 0,
    };

    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);

    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);

    /// <summary>
    /// Representação de diagnóstico, sem aplicar máscara de formatação.
    /// A formatação para exibição é responsabilidade do formatador de números.
    /// </summary>
    public override string ToString() => Kind switch
    {
        CellValueKind.Blank => string.Empty,
        CellValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        CellValueKind.Text => _text!,
        CellValueKind.Logical => _number != 0d ? "VERDADEIRO" : "FALSO",
        CellValueKind.Error => _error.ToDisplayText(),
        _ => string.Empty,
    };
}

using System.Diagnostics.CodeAnalysis;

namespace Nordxcel.Core.Model;

/// <summary>
/// Conjunto de abas de um modelo. Nomes de aba são únicos sem diferenciar
/// maiúsculas de minúsculas, como no Excel, porque as fórmulas resolvem
/// <c>Premissas!B5</c> e <c>premissas!B5</c> para a mesma aba.
/// </summary>
public sealed class Workbook
{
    private readonly List<Worksheet> _worksheets = [];
    private readonly Dictionary<string, Worksheet> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Abas na ordem em que aparecem, da esquerda para a direita.</summary>
    public IReadOnlyList<Worksheet> Worksheets => _worksheets;

    /// <summary>Aba pelo nome, sem diferenciar maiúsculas de minúsculas.</summary>
    public Worksheet this[string name] =>
        TryGetWorksheet(name, out Worksheet? worksheet)
            ? worksheet
            : throw new KeyNotFoundException($"A pasta não tem uma aba chamada '{name}'.");

    /// <summary>Cria uma pasta nova com uma única aba vazia, como ao abrir o aplicativo.</summary>
    public static Workbook CreateDefault()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Planilha1");
        return workbook;
    }

    public Worksheet AddWorksheet(string name)
    {
        string validated = Worksheet.ValidateName(name);

        if (_byName.ContainsKey(validated))
        {
            throw new ArgumentException($"Já existe uma aba chamada '{validated}'.", nameof(name));
        }

        var worksheet = new Worksheet(validated);
        _worksheets.Add(worksheet);
        _byName.Add(validated, worksheet);

        return worksheet;
    }

    /// <summary>Adiciona uma aba com nome gerado automaticamente, como o botão "+" da barra de abas.</summary>
    public Worksheet AddWorksheet() => AddWorksheet(CreateUniqueWorksheetName());

    /// <summary>
    /// Gera um nome de aba livre a partir do prefixo: <c>Planilha2</c> quando
    /// <c>Planilha1</c> já existe, e assim por diante.
    /// </summary>
    public string CreateUniqueWorksheetName(string prefix = "Planilha")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        // Existem N abas no máximo, então N+1 candidatos garantem achar um livre
        // pela casa dos pombos, mesmo que nenhuma aba já siga esse padrão de nome.
        for (int i = 1; i <= _worksheets.Count + 1; i++)
        {
            string candidate = $"{prefix}{i}";

            if (!ContainsWorksheet(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Não foi possível gerar um nome de aba livre.");
    }

    public bool TryGetWorksheet(string name, [NotNullWhen(true)] out Worksheet? worksheet)
    {
        if (string.IsNullOrEmpty(name))
        {
            worksheet = null;
            return false;
        }

        return _byName.TryGetValue(name, out worksheet);
    }

    public bool ContainsWorksheet(string name) => TryGetWorksheet(name, out _);

    public bool RemoveWorksheet(string name)
    {
        if (!TryGetWorksheet(name, out Worksheet? worksheet))
        {
            return false;
        }

        _byName.Remove(worksheet.Name);
        _worksheets.Remove(worksheet);

        return true;
    }

    /// <summary>
    /// Renomeia a aba mantendo a unicidade. Renomear para o mesmo nome trocando
    /// apenas a caixa das letras é permitido.
    /// </summary>
    public void RenameWorksheet(string currentName, string newName)
    {
        if (!TryGetWorksheet(currentName, out Worksheet? worksheet))
        {
            throw new KeyNotFoundException($"A pasta não tem uma aba chamada '{currentName}'.");
        }

        string validated = Worksheet.ValidateName(newName);

        if (_byName.TryGetValue(validated, out Worksheet? existing) && !ReferenceEquals(existing, worksheet))
        {
            throw new ArgumentException($"Já existe uma aba chamada '{validated}'.", nameof(newName));
        }

        _byName.Remove(worksheet.Name);
        worksheet.Name = validated;
        _byName[validated] = worksheet;
    }

    /// <summary>
    /// Cópia independente da pasta inteira, abas e células — nada nela é
    /// compartilhado com o original. Usado pela Tabela de Dados para recalcular o
    /// modelo com premissas substituídas sem afetar o que o usuário vê na tela.
    /// </summary>
    public Workbook Clone()
    {
        var clone = new Workbook();

        foreach (Worksheet sheet in _worksheets)
        {
            Worksheet sheetClone = sheet.Clone();
            clone._worksheets.Add(sheetClone);
            clone._byName.Add(sheetClone.Name, sheetClone);
        }

        return clone;
    }

    /// <summary>Move a aba para outra posição na barra de abas.</summary>
    public void MoveWorksheet(string name, int newIndex)
    {
        if (!TryGetWorksheet(name, out Worksheet? worksheet))
        {
            throw new KeyNotFoundException($"A pasta não tem uma aba chamada '{name}'.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _worksheets.Count);

        _worksheets.Remove(worksheet);
        _worksheets.Insert(newIndex, worksheet);
    }
}

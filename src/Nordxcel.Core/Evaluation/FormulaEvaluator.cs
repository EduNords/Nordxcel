using Nordxcel.Core.Evaluation.Functions;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// Terceira etapa do motor: percorre a árvore sintática e produz um valor.
/// <para>
/// Não sabe nada sobre ordem de recálculo nem sobre dependências — recebe uma
/// árvore e um contexto de leitura e devolve o resultado. Quem decide o que
/// recalcular e em que ordem é o grafo de dependências.
/// </para>
/// </summary>
public sealed class FormulaEvaluator
{
    private readonly FormulaSyntax _syntax;
    private readonly IFunctionRegistry _functions;

    public FormulaEvaluator(
        IEvaluationContext context,
        IFunctionRegistry? functions = null,
        FormulaSyntax? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        _functions = functions ?? FunctionRegistry.Standard;
        _syntax = syntax ?? FormulaSyntax.Default;
    }

    /// <summary>De onde os valores das células são lidos.</summary>
    public IEvaluationContext Context { get; }

    /// <summary>Convenção de escrita em uso, necessária para coagir texto em número.</summary>
    public FormulaSyntax Syntax => _syntax;

    /// <summary>Avalia a fórmula e reduz o resultado a um único valor de célula.</summary>
    public CellValue Evaluate(FormulaNode node, EvaluationScope scope) =>
        EvaluateOperand(node, scope).ToScalar();

    /// <summary>
    /// Avalia a fórmula preservando intervalo como intervalo. É o que as funções
    /// de agregação precisam ver.
    /// </summary>
    public FormulaValue EvaluateOperand(FormulaNode node, EvaluationScope scope)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            NumberNode number => FormulaValue.FromScalar(CellValue.Number(number.Value)),
            TextNode text => FormulaValue.FromScalar(CellValue.Text(text.Value)),
            LogicalNode logical => FormulaValue.FromScalar(CellValue.Logical(logical.Value)),
            ErrorNode error => FormulaValue.FromError(error.Error),

            // Argumento omitido se comporta como célula vazia.
            MissingArgumentNode => FormulaValue.FromScalar(CellValue.Blank),

            // Intervalo nomeado ainda não existe; qualquer nome solto é desconhecido.
            NameNode => FormulaValue.FromError(CellErrorType.Name),

            ReferenceNode reference => EvaluateReference(reference, scope),
            RangeNode range => EvaluateRange(range, scope),
            UnaryNode unary => EvaluateUnary(unary, scope),
            BinaryNode binary => EvaluateBinary(binary, scope),
            FunctionNode function => EvaluateFunction(function, scope),

            _ => throw new NotSupportedException($"Nó de fórmula não suportado: {node.GetType().Name}."),
        };
    }

    private FormulaValue EvaluateReference(ReferenceNode node, EvaluationScope scope)
    {
        string sheet = scope.ResolveSheet(node.Reference.Sheet);

        return Context.ContainsSheet(sheet)
            ? FormulaValue.FromScalar(Context.GetValue(sheet, node.Reference.Address))
            : FormulaValue.FromError(CellErrorType.Reference);
    }

    private FormulaValue EvaluateRange(RangeNode node, EvaluationScope scope)
    {
        string sheet = scope.ResolveSheet(node.Sheet);

        return Context.ContainsSheet(sheet)
            ? FormulaValue.FromRange(sheet, node.ToRange())
            : FormulaValue.FromError(CellErrorType.Reference);
    }

    private FormulaValue EvaluateUnary(UnaryNode node, EvaluationScope scope)
    {
        CellValue operand = Evaluate(node.Operand, scope);

        if (operand.IsError)
        {
            return FormulaValue.FromScalar(operand);
        }

        if (!ValueCoercion.TryToNumber(operand, _syntax, out double number, out CellErrorType error))
        {
            return FormulaValue.FromError(error);
        }

        double result = node.Operator switch
        {
            UnaryOperator.Plus => number,
            UnaryOperator.Negate => -number,
            UnaryOperator.Percent => number / 100d,
            _ => throw new NotSupportedException($"Operador unário não suportado: {node.Operator}."),
        };

        return FromNumber(result);
    }

    private FormulaValue EvaluateBinary(BinaryNode node, EvaluationScope scope)
    {
        CellValue left = Evaluate(node.Left, scope);

        // O Excel propaga o erro mais à esquerda; parar aqui evita trabalho inútil.
        if (left.IsError)
        {
            return FormulaValue.FromScalar(left);
        }

        CellValue right = Evaluate(node.Right, scope);

        if (right.IsError)
        {
            return FormulaValue.FromScalar(right);
        }

        return node.Operator switch
        {
            BinaryOperator.Concat => EvaluateConcat(left, right),

            BinaryOperator.Equal or
            BinaryOperator.NotEqual or
            BinaryOperator.Less or
            BinaryOperator.LessOrEqual or
            BinaryOperator.Greater or
            BinaryOperator.GreaterOrEqual => EvaluateComparison(node.Operator, left, right),

            _ => EvaluateArithmetic(node.Operator, left, right),
        };
    }

    private FormulaValue EvaluateConcat(CellValue left, CellValue right)
    {
        if (!ValueCoercion.TryToText(left, _syntax, out string leftText, out CellErrorType error) ||
            !ValueCoercion.TryToText(right, _syntax, out string rightText, out error))
        {
            return FormulaValue.FromError(error);
        }

        return FormulaValue.FromScalar(CellValue.Text(leftText + rightText));
    }

    private static FormulaValue EvaluateComparison(BinaryOperator op, CellValue left, CellValue right)
    {
        int comparison = ValueCoercion.Compare(left, right);

        bool result = op switch
        {
            BinaryOperator.Equal => comparison == 0,
            BinaryOperator.NotEqual => comparison != 0,
            BinaryOperator.Less => comparison < 0,
            BinaryOperator.LessOrEqual => comparison <= 0,
            BinaryOperator.Greater => comparison > 0,
            BinaryOperator.GreaterOrEqual => comparison >= 0,
            _ => throw new NotSupportedException($"Comparador não suportado: {op}."),
        };

        return FormulaValue.FromScalar(CellValue.Logical(result));
    }

    private FormulaValue EvaluateArithmetic(BinaryOperator op, CellValue left, CellValue right)
    {
        if (!ValueCoercion.TryToNumber(left, _syntax, out double a, out CellErrorType error) ||
            !ValueCoercion.TryToNumber(right, _syntax, out double b, out error))
        {
            return FormulaValue.FromError(error);
        }

        switch (op)
        {
            case BinaryOperator.Add:
                return FromNumber(a + b);

            case BinaryOperator.Subtract:
                return FromNumber(a - b);

            case BinaryOperator.Multiply:
                return FromNumber(a * b);

            case BinaryOperator.Divide:
                return b == 0d
                    ? FormulaValue.FromError(CellErrorType.DivideByZero)
                    : FromNumber(a / b);

            case BinaryOperator.Power:
                return FormulaValue.FromScalar(NumericResult.Power(a, b));

            default:
                throw new NotSupportedException($"Operador aritmético não suportado: {op}.");
        }
    }

    private FormulaValue EvaluateFunction(FunctionNode node, EvaluationScope scope)
    {
        if (!_functions.TryGetFunction(node.Name, out IFormulaFunction? function) || function is null)
        {
            return FormulaValue.FromError(CellErrorType.Name);
        }

        if (node.Arguments.Count < function.MinArguments || node.Arguments.Count > function.MaxArguments)
        {
            // O Excel nem deixa confirmar a fórmula nesse caso; aqui o erro fica na célula.
            return FormulaValue.FromError(CellErrorType.Value);
        }

        var call = new FunctionCall(node.Name, node.Arguments, this, scope);

        return FormulaValue.FromScalar(function.Invoke(call));
    }

    private static FormulaValue FromNumber(double value) =>
        FormulaValue.FromScalar(NumericResult.FromDouble(value));
}

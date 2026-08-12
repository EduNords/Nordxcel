# Roadmap Técnico — MVP Planilha para DCF

**Stack:** C# / .NET 8+ / Avalonia UI / SkiaSharp (grid) / ClosedXML ou EPPlus (exportação .xlsx)
**Plataformas:** Windows, macOS (Intel + Apple Silicon), Linux
**Escopo:** Modelagem DCF apenas. LBO fica para versão futura (referências circulares, cash sweep).

---

## 1. Escopo funcional completo

### 1.1 Motor de cálculo

**Referenciamento de células**
- Relativo (`A1`), absoluto (`$A$1`), misto (`A$1`, `$A1`)
- Referência entre abas (`Premissas!B5`)
- Ranges (`B2:B10`) para funções de agregação

**Funções necessárias para DCF**
| Categoria | Funções |
|---|---|
| Agregação | `SOMA`, `MÉDIA`, `MÍNIMO`, `MÁXIMO`, `CONT.VALORES` |
| Financeiras | `VPL` (NPV), `TIR` (IRR), `VF` (crescimento/perpetuidade), `TAXA` |
| Lógicas | `SE`, `E`, `OU`, `SEERRO` (IFERROR — evita `#DIV/0!` quebrando o modelo) |
| Matemáticas | `ARRED`, `ABS`, `POTÊNCIA`, `RAIZ` |
| Referência | `ÍNDICE`/`CORRESP` — opcional no MVP, mas comum em modelos de premissas com seletor de cenário |

**Análise de sensibilidade (Data Table)**
- 1 variável (ex.: WACC variando → valor da empresa)
- 2 variáveis (ex.: WACC × taxa de crescimento na perpetuidade → matriz de valuation)
- Precisa de um "shadow calculation": recalcular a planilha N vezes com inputs substituídos, sem alterar o estado real exibido ao usuário

**Motor de dependências**
- Grafo direcionado acíclico (DAG) — DCF não tem circularidade, então dá pra simplificar: **detectar e recusar ciclos** em vez de suportar cálculo iterativo (isso fica para o LBO)
- Recálculo incremental (dirty propagation): só recalcula quem depende da célula alterada

### 1.2 Formatação de célula

**Fonte e preenchimento**
- Negrito, itálico, sublinhado
- Cor da fonte, cor de fundo
- Tamanho e família da fonte (mínimo: 2-3 fontes padrão tipo Calibri/Arial)
- Alinhamento horizontal/vertical

**Bordas**
- Por lado individual: superior, inferior, esquerda, direita
- "Todas as bordas" e "borda externa" como atalhos de UI
- Espessura (fina/grossa) e estilo (contínua/dupla — dupla é comum em linha de "total geral" em modelos financeiros)

**Dimensões**
- Largura de coluna, altura de linha
- Aplicar a coluna/linha inteira de uma vez (clique no cabeçalho)
- Congelar painéis (essencial para modelos DCF longos — travar coluna de labels e linha de cabeçalho de anos)

**Formatos de número (o ponto que você destacou — crítico para modelagem)**

| Formato | Exemplo positivo | Exemplo negativo | Observação |
|---|---|---|---|
| Número com milhar | `1.234.567` | `(1.234.567)` | Parênteses em vez de sinal — padrão de IB/PE |
| Moeda | `R$ 1.234.567` ou `$1,234,567` | `(R$ 1.234.567)` | Suporte a símbolo customizável (R$, $, €) |
| Porcentagem | `12,5%` | `(12,5%)` | Negativo também em parênteses, não com `-` |
| Múltiplo | `10,2x` | `(10,2x)` | Sufixo `x` customizado — não é formato nativo do Excel, precisa implementar como máscara própria |
| Decimal customizável | `0`, `1`, `2` casas | — | Toggle rápido de "aumentar/diminuir casas decimais" |
| Data | `dd/mm/aaaa` | — | Para linha do tempo de projeção anual/trimestral |

Tecnicamente, isso é implementado como uma **string de máscara por célula** (parecido com o "Formato de Número Customizado" do próprio Excel: `#,##0;(#,##0)` já é a sintaxe que gera exatamente esse comportamento de parênteses). Vale reaproveitar essa sintaxe internamente — assim a exportação para `.xlsx` fica trivial (é literalmente o mesmo formato).

**Sistema de cores automático (diferencial que discutimos antes)**
- Azul: célula é input manual (sem fórmula)
- Preto: célula contém fórmula referenciando a mesma aba
- Verde: célula contém fórmula referenciando outra aba
- Aplicado automaticamente ao digitar, sobrescrevível manualmente

### 1.3 Estrutura de template DCF guiado

- Seções pré-formatadas: Receita → Custos → EBITDA → D&A → EBIT → Impostos → NOPAT → Capex → Δ Capital de Giro → FCF Livre
- Bloco de premissas separado (WACC, taxa de crescimento na perpetuidade, taxa de imposto)
- Bloco de valuation: soma dos FCFs descontados + valor terminal (Gordon Growth ou Exit Multiple) + Enterprise Value → Equity Value
- Checkpoints de validação: comparação do valor calculado pelo aluno com um gabarito, destacando a linha divergente

### 1.4 Fora do escopo do MVP (fica para depois)

- LBO completo (cash sweep circular)
- VBA/scripting
- Gráficos
- Colaboração em tempo real
- Importação de `.xlsx` complexo de terceiros (só exportação no MVP)
- Funções de texto/lookup avançadas (`PROCV`, `PROCX`, concatenação)

---

## 2. Arquitetura técnica

```
┌────────────────────────────────────────────┐
│  Avalonia UI (XAML + C#)                    │
│  - Toolbar de formatação                    │
│  - Barra de fórmulas                        │
│  - Painel de premissas / cenários           │
└──────────────────┬───────────────────────────┘
                    │
┌──────────────────┴───────────────────────────┐
│  Grid customizado (SkiaSharp)                │
│  - Renderização virtualizada (só visível)    │
│  - Seleção, edição inline, resize col/linha  │
│  - Congelar painéis                          │
└──────────────────┬───────────────────────────┘
                    │
┌──────────────────┴───────────────────────────┐
│  Core de domínio (C# puro, sem dependência   │
│  de UI — testável isoladamente)              │
│  - CellStore: Dictionary<(int,int), Cell>    │
│  - Parser de fórmula (Tokenizer → AST)       │
│  - Grafo de dependências (DAG)               │
│  - Motor de recálculo incremental            │
│  - Motor de Data Table (shadow calc)         │
│  - Formatador de número (máscara custom)     │
└──────────────────┬───────────────────────────┘
                    │
              Exportação → ClosedXML/EPPlus (.xlsx)
```

**Por que separar o Core do resto:** isso permite testar o motor de cálculo com testes unitários (ex.: "dado esse DCF de exemplo, o valor presente líquido bate com X") sem precisar renderizar UI nenhuma — acelera muito o desenvolvimento e a confiança no motor.

---

## 3. Fases e estimativas (dev solo, avançado)

| Fase | Entregável | Estimativa |
|---|---|---|
| **0. Setup** | Projeto Avalonia + estrutura de solução (Core / UI / Tests separados) 
| **1. Core de dados** | `CellStore`, modelo de `Cell` (valor, fórmula, formato, estilo), sem UI ainda 
| **2. Parser de fórmula** | Tokenizer + AST + avaliador para operadores e funções da tabela acima 
| **3. Grafo de dependências** | DAG, detecção de ciclo, recálculo incremental (dirty propagation)
| **4. Testes do motor** | Suite de testes com um DCF real de exemplo validando os números 
| **5. Grid visual (SkiaSharp)** | Renderização virtualizada, seleção, edição inline, resize, congelar painéis 
| **6. Formatação completa** | Negrito/itálico/cores/bordas + formatos numéricos (parênteses, %, múltiplo) + cores automáticas input/fórmula/link
| **7. Data Table (sensibilidade)** | Shadow calculation 1 e 2 variáveis, renderização da matriz de resultados 
| **8. Template DCF guiado** | Estrutura pré-formatada + sistema de checkpoint/validação contra gabarito 
| **9. Exportação .xlsx** | ClosedXML/EPPlus preservando fórmulas, formatos e bordas 
| **10. Empacotamento** | Build self-contained Win/Mac(x64+arm64)/Linux, code signing Mac
| **11. Testes com usuários reais** | 5-10 estudantes de modelagem testando, ajustes de UX 

**Total estimado: ~15-19 semanas (aproximadamente 4-4,5 meses) full-time solo.**

### Ordem de construção recomendada
Fases 1→4 (o Core inteiro, sem UI) antes da Fase 5. Validar que o motor de cálculo produz os números certos de um DCF real usando testes automatizados é mais barato e mais rápido de corrigir do que descobrir bugs de cálculo depois que já existe grid bonito por cima. UI vem depois que o motor está confiável.

---

## 4. Bibliotecas C# relevantes

- **Avalonia** — framework de UI cross-platform
- **SkiaSharp** — renderização de canvas de alta performance (já vem integrado com Avalonia)
- **ClosedXML** (mais simples de usar) ou **EPPlus** (mais completo, mas licença paga acima de certo faturamento — verificar termos atuais antes de decidir) — exportação `.xlsx`
- **xUnit** ou **NUnit** — testes do motor de cálculo
- NPV/IRR: implementação própria (fórmulas financeiras padrão, poucas linhas de código, não precisa de lib externa)
using System;
using System.Globalization;

namespace Kasir.Services
{
    public enum CalculatorOperator { Add, Subtract, Multiply, Divide }

    /// <summary>
    /// Pocket-calculator logic for the F9 Kalkulator on the sale screen.
    ///
    /// Behaves like a basic calculator / phone calculator app ("immediate execution"):
    /// each operator applies the pending operation first, so evaluation is strictly left
    /// to right with no precedence: 2 + 3 × 4 = 20. Pressing an operator shows the running
    /// result. '=' repeated re-applies the last operation (5 + 3 = = = → 14). '=' with no
    /// second number uses the first (5 + = → 10). Pressing another operator straight after
    /// one replaces it. A digit after '=' starts a new calculation; an operator after '='
    /// continues from the result.
    ///
    /// All arithmetic is <see cref="decimal"/> (no floating point): 0,1 + 0,2 = 0,3.
    /// Display uses Indonesian formatting ('.' thousands, ',' decimal), results rounded to
    /// 4 decimals for display only. Division by zero and results beyond 18 digits put the
    /// calculator in an error state with a message instead of throwing; a digit or Clear
    /// starts over.
    /// </summary>
    public class CalculatorEngine
    {
        public const int MaxEntryDigits = 15;
        public const string DivideByZeroMessage = "Tidak bisa dibagi 0";
        public const string TooLargeMessage = "Angka terlalu besar";

        private static readonly CultureInfo Indonesian = new CultureInfo("id-ID");
        private static readonly decimal Limit = 1_000_000_000_000_000_000m; // 10^18

        private decimal _accumulator;
        private CalculatorOperator? _pending;
        private string _entry = "";          // digits typed so far, ',' as decimal mark; "" = none
        private bool _justEvaluated;
        private CalculatorOperator? _lastOp;  // for repeated '='
        private decimal _lastOperand;
        private string _error;

        /// <summary>Big number shown on the calculator (or the error message).</summary>
        public string Display { get; private set; } = "0";

        /// <summary>Running expression line above the display, e.g. "1.500 ×" or "1.500 × 3 =".</summary>
        public string Expression { get; private set; } = "";

        public bool HasError => _error != null;

        /// <summary>The number currently shown (entry being typed, or last result).</summary>
        public decimal Value => _entry.Length > 0 ? ParseEntry(_entry) : _accumulator;

        public void Digit(int d)
        {
            if (d < 0 || d > 9) throw new ArgumentOutOfRangeException(nameof(d));
            StartFreshIfDone();
            if (CountDigits(_entry) >= MaxEntryDigits) return;
            if (_entry == "0") _entry = "";
            if (_entry.Length == 0 && d == 0) { _entry = "0"; Refresh(); return; }
            _entry += (char)('0' + d);
            Refresh();
        }

        public void TripleZero()
        {
            for (int i = 0; i < 3; i++) Digit(0);
        }

        public void DecimalPoint()
        {
            StartFreshIfDone();
            if (_entry.IndexOf(',') >= 0) return;
            _entry = (_entry.Length == 0 ? "0" : _entry) + ",";
            Refresh();
        }

        public void Operator(CalculatorOperator op)
        {
            if (HasError) return;
            if (_entry.Length > 0)
            {
                decimal operand = ParseEntry(_entry);
                if (_pending.HasValue)
                {
                    if (!TryApply(_accumulator, _pending.Value, operand, out decimal r)) return;
                    _accumulator = r;
                }
                else
                {
                    _accumulator = operand;
                }
                _entry = "";
            }
            // No new number: either continue from the last result / zero, or the user
            // changed their mind about the operator, which simply replaces it.
            _pending = op;
            _justEvaluated = false;
            Expression = Format(_accumulator) + " " + Symbol(op);
            Display = Format(_accumulator);
        }

        public void Evaluate()
        {
            if (HasError) return;
            if (_pending.HasValue)
            {
                decimal left = _accumulator;
                decimal operand = _entry.Length > 0 ? ParseEntry(_entry) : _accumulator;
                CalculatorOperator op = _pending.Value;
                if (!TryApply(left, op, operand, out decimal r)) return;
                _lastOp = op;
                _lastOperand = operand;
                _accumulator = r;
                Expression = Format(left) + " " + Symbol(op) + " " + Format(operand) + " =";
            }
            else if (_justEvaluated && _lastOp.HasValue)
            {
                decimal left = _accumulator;
                if (!TryApply(left, _lastOp.Value, _lastOperand, out decimal r)) return;
                _accumulator = r;
                Expression = Format(left) + " " + Symbol(_lastOp.Value) + " " + Format(_lastOperand) + " =";
            }
            else if (_entry.Length > 0)
            {
                _accumulator = ParseEntry(_entry);
                Expression = Format(_accumulator) + " =";
            }
            else
            {
                return;
            }
            _entry = "";
            _pending = null;
            _justEvaluated = true;
            Display = Format(_accumulator);
        }

        public void Backspace()
        {
            if (HasError) { Clear(); return; }
            if (_justEvaluated || _entry.Length == 0) return;
            _entry = _entry.Substring(0, _entry.Length - 1);
            Refresh();
        }

        public void Clear()
        {
            _accumulator = 0m;
            _pending = null;
            _entry = "";
            _justEvaluated = false;
            _lastOp = null;
            _lastOperand = 0m;
            _error = null;
            Display = "0";
            Expression = "";
        }

        /// <summary>Indonesian display format: 1234567,5 → "1.234.567,5"; rounded to 4 decimals.</summary>
        public static string Format(decimal value)
        {
            decimal rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            if (rounded == 0m) rounded = 0m; // no "-0"
            return rounded.ToString("#,##0.####", Indonesian);
        }

        private void StartFreshIfDone()
        {
            if (HasError || _justEvaluated) Clear();
        }

        private bool TryApply(decimal left, CalculatorOperator op, decimal right, out decimal result)
        {
            result = 0m;
            if (op == CalculatorOperator.Divide && right == 0m) { SetError(DivideByZeroMessage); return false; }
            try
            {
                result = op switch
                {
                    CalculatorOperator.Add => left + right,
                    CalculatorOperator.Subtract => left - right,
                    CalculatorOperator.Multiply => left * right,
                    _ => left / right,
                };
            }
            catch (OverflowException)
            {
                SetError(TooLargeMessage);
                return false;
            }
            if (Math.Abs(result) >= Limit) { SetError(TooLargeMessage); return false; }
            return true;
        }

        private void SetError(string message)
        {
            Clear();
            _error = message;
            Display = message;
        }

        private void Refresh()
        {
            Display = _entry.Length == 0 ? "0" : FormatEntry(_entry);
        }

        // Keeps what was typed after the comma (trailing zeros, a bare comma) visible.
        private static string FormatEntry(string entry)
        {
            int comma = entry.IndexOf(',');
            string whole = comma >= 0 ? entry.Substring(0, comma) : entry;
            string wholeText = decimal.Parse(whole.Length == 0 ? "0" : whole, CultureInfo.InvariantCulture)
                .ToString("#,##0", Indonesian);
            return comma >= 0 ? wholeText + entry.Substring(comma) : wholeText;
        }

        private static decimal ParseEntry(string entry)
        {
            string s = entry.Replace(',', '.');
            if (s.EndsWith(".")) s = s.Substring(0, s.Length - 1);
            return decimal.Parse(s.Length == 0 ? "0" : s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private static int CountDigits(string entry)
        {
            int n = 0;
            foreach (char c in entry) if (c >= '0' && c <= '9') n++;
            return n;
        }

        public static string Symbol(CalculatorOperator op) => op switch
        {
            CalculatorOperator.Add => "+",
            CalculatorOperator.Subtract => "−",
            CalculatorOperator.Multiply => "×",
            _ => "÷",
        };
    }
}

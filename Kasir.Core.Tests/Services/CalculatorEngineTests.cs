using NUnit.Framework;
using FluentAssertions;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    // F9 Kalkulator on the sale screen (owner report #17). Behaves like a basic
    // pocket / phone calculator: immediate execution, left to right
    // (2 + 3 × 4 = 20, not 14).
    [TestFixture]
    public class CalculatorEngineTests
    {
        private CalculatorEngine _calc;

        [SetUp]
        public void SetUp() => _calc = new CalculatorEngine();

        // Feeds keys like a user would: digits, ',' '+' '-' '*' '/' '=' 'C' '<' (backspace), 'Z' (000).
        private void Keys(string keys)
        {
            foreach (char k in keys)
            {
                switch (k)
                {
                    case >= '0' and <= '9': _calc.Digit(k - '0'); break;
                    case ',': _calc.DecimalPoint(); break;
                    case '+': _calc.Operator(CalculatorOperator.Add); break;
                    case '-': _calc.Operator(CalculatorOperator.Subtract); break;
                    case '*': _calc.Operator(CalculatorOperator.Multiply); break;
                    case '/': _calc.Operator(CalculatorOperator.Divide); break;
                    case '=': _calc.Evaluate(); break;
                    case 'C': _calc.Clear(); break;
                    case '<': _calc.Backspace(); break;
                    case 'Z': _calc.TripleZero(); break;
                    case ' ': break;
                }
            }
        }

        [Test]
        public void Initially_ShowsZero_EmptyExpression()
        {
            _calc.Display.Should().Be("0");
            _calc.Expression.Should().BeEmpty();
            _calc.Value.Should().Be(0m);
            _calc.HasError.Should().BeFalse();
        }

        [Test]
        public void Digits_AreFormattedWithIndonesianThousands()
        {
            Keys("1250000");
            _calc.Display.Should().Be("1.250.000");
            _calc.Value.Should().Be(1250000m);
        }

        [Test]
        public void LeadingZeros_AreDropped()
        {
            Keys("0007");
            _calc.Display.Should().Be("7");
        }

        [Test]
        public void TripleZero_AppendsThreeZeros()
        {
            Keys("25Z");
            _calc.Display.Should().Be("25.000");
        }

        [Test]
        public void TripleZero_OnEmptyEntry_StaysZero()
        {
            Keys("Z");
            _calc.Display.Should().Be("0");
        }

        [TestCase("12+7=", "19")]
        [TestCase("12-20=", "-8")]
        [TestCase("12Z*3=", "36.000")]
        [TestCase("100/8=", "12,5")]
        public void BasicOperations(string keys, string expected)
        {
            Keys(keys);
            _calc.Display.Should().Be(expected);
        }

        [Test]
        public void ChainedOperators_EvaluateLeftToRight_ImmediateExecution()
        {
            Keys("2+3*4=");
            _calc.Display.Should().Be("20");
        }

        [Test]
        public void PressingOperator_ShowsRunningResult()
        {
            Keys("2+3*");
            _calc.Display.Should().Be("5");
            _calc.Expression.Should().Be("5 ×");
        }

        [Test]
        public void Expression_ShowsPendingOperation()
        {
            Keys("1500+");
            _calc.Expression.Should().Be("1.500 +");
            Keys("250");
            _calc.Display.Should().Be("250");
            _calc.Expression.Should().Be("1.500 +");
        }

        [Test]
        public void Expression_AfterEquals_ShowsFullCalculation()
        {
            Keys("1500*3=");
            _calc.Expression.Should().Be("1.500 × 3 =");
            _calc.Display.Should().Be("4.500");
        }

        [Test]
        public void ChangingOperator_ReplacesPendingOperator()
        {
            Keys("10+-*4=");
            _calc.Display.Should().Be("40");
        }

        [Test]
        public void RepeatedEquals_RepeatsLastOperation()
        {
            Keys("5+3===");
            _calc.Display.Should().Be("14");
            _calc.Expression.Should().Be("11 + 3 =");
        }

        [Test]
        public void RepeatedEquals_WithMultiply()
        {
            Keys("2*3==");
            _calc.Display.Should().Be("18");
        }

        [Test]
        public void OperatorAfterEquals_ContinuesFromResult()
        {
            Keys("5+5=*2=");
            _calc.Display.Should().Be("20");
        }

        [Test]
        public void DigitAfterEquals_StartsNewCalculation()
        {
            Keys("5+5=7");
            _calc.Display.Should().Be("7");
            _calc.Expression.Should().BeEmpty();
            Keys("+1=");
            _calc.Display.Should().Be("8");
        }

        [Test]
        public void EqualsWithoutSecondOperand_UsesFirstOperand()
        {
            Keys("5+=");
            _calc.Display.Should().Be("10");
        }

        [Test]
        public void Decimal_InputShowsComma()
        {
            Keys("1234,5");
            _calc.Display.Should().Be("1.234,5");
            _calc.Value.Should().Be(1234.5m);
        }

        [Test]
        public void Decimal_TrailingCommaAndZerosAreKeptWhileTyping()
        {
            Keys("3,");
            _calc.Display.Should().Be("3,");
            Keys("0");
            _calc.Display.Should().Be("3,0");
        }

        [Test]
        public void Decimal_FirstKey_StartsWithZero()
        {
            Keys(",5");
            _calc.Display.Should().Be("0,5");
        }

        [Test]
        public void Decimal_SecondCommaIgnored()
        {
            Keys("1,2,3");
            _calc.Display.Should().Be("1,23");
        }

        [Test]
        public void Decimal_ArithmeticIsExact_NoFloatingPointError()
        {
            Keys("0,1+0,2=");
            _calc.Display.Should().Be("0,3");
            _calc.Value.Should().Be(0.3m);
        }

        [Test]
        public void Division_ResultIsRoundedForDisplay()
        {
            Keys("10/3=");
            _calc.Display.Should().Be("3,3333");
        }

        [Test]
        public void Division_RepeatingResult_MultipliedBack_IsWhole()
        {
            Keys("10/3*3=");
            _calc.Display.Should().Be("10");
        }

        [Test]
        public void Backspace_RemovesLastDigit()
        {
            Keys("1234<");
            _calc.Display.Should().Be("123");
            Keys("<<<");
            _calc.Display.Should().Be("0");
            Keys("<");
            _calc.Display.Should().Be("0");
        }

        [Test]
        public void Backspace_RemovesDecimalComma()
        {
            Keys("5,<");
            _calc.Display.Should().Be("5");
            Keys("2");
            _calc.Display.Should().Be("52");
        }

        [Test]
        public void Backspace_AfterEquals_DoesNotChangeResult()
        {
            Keys("12+3=<");
            _calc.Display.Should().Be("15");
        }

        [Test]
        public void Clear_ResetsEverything()
        {
            Keys("12+3*C");
            _calc.Display.Should().Be("0");
            _calc.Expression.Should().BeEmpty();
            Keys("4=");
            _calc.Display.Should().Be("4");
        }

        [Test]
        public void DivideByZero_ShowsMessage_DoesNotThrow()
        {
            Keys("5/0=");
            _calc.HasError.Should().BeTrue();
            _calc.Display.Should().Be("Tidak bisa dibagi 0");
        }

        [Test]
        public void DivideByZero_ViaChainedOperator_ShowsMessage()
        {
            Keys("5/0+");
            _calc.HasError.Should().BeTrue();
        }

        [Test]
        public void AfterError_DigitStartsFresh()
        {
            Keys("5/0=7+1=");
            _calc.HasError.Should().BeFalse();
            _calc.Display.Should().Be("8");
        }

        [Test]
        public void AfterError_OperatorsAreIgnored_UntilCleared()
        {
            Keys("5/0=+=");
            _calc.HasError.Should().BeTrue();
            Keys("C");
            _calc.HasError.Should().BeFalse();
            _calc.Display.Should().Be("0");
        }

        [Test]
        public void LargeNumbers_StayExact()
        {
            Keys("999999999999*1000=");
            _calc.Display.Should().Be("999.999.999.999.000");
        }

        [Test]
        public void Entry_IsCappedAt15Digits()
        {
            Keys("12345678901234567890");
            _calc.Display.Should().Be("123.456.789.012.345");
        }

        [Test]
        public void Overflow_ShowsMessage_DoesNotThrow()
        {
            // Beyond decimal's range: must not throw OverflowException.
            Keys("999999999999999*999999999999999=");
            _calc.HasError.Should().BeTrue();
            _calc.Display.Should().Be("Angka terlalu besar");
        }

        [Test]
        public void ResultAbove18Digits_ShowsMessage()
        {
            Keys("999999999999*9999999=");
            _calc.HasError.Should().BeTrue();
            _calc.Display.Should().Be("Angka terlalu besar");
        }

        [Test]
        public void StartingWithMinus_SubtractsFromZero()
        {
            Keys("-5=");
            _calc.Display.Should().Be("-5");
        }

        [Test]
        public void NegativeResult_IsFormatted()
        {
            Keys("1000-2500=");
            _calc.Display.Should().Be("-1.500");
        }
    }
}

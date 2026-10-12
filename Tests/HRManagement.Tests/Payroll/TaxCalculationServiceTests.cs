using HRManagement.Services.Payroll;

namespace HRManagement.Tests.Payroll
{
    public class TaxCalculationServiceTests
    {
        private readonly TaxCalculationService _service = new();

        // Expected amounts are worked out bracket by bracket (5% of the first 5M, 10% of the next 5M, ...),
        // independently of the quick-deduction formula the service uses.
        private static decimal ProgressiveTax(decimal taxable)
        {
            (decimal Upper, decimal Rate)[] brackets =
            {
                (5_000_000, 0.05m), (10_000_000, 0.10m), (18_000_000, 0.15m), (32_000_000, 0.20m),
                (52_000_000, 0.25m), (80_000_000, 0.30m), (decimal.MaxValue, 0.35m)
            };
            decimal tax = 0, lower = 0;
            foreach (var (upper, rate) in brackets)
            {
                if (taxable <= lower) break;
                tax += (Math.Min(taxable, upper) - lower) * rate;
                lower = upper;
            }
            return tax;
        }

        [Fact]
        public void Salary_of_30M_without_dependents_is_taxed_in_bracket_3()
        {
            var result = _service.Calculate(30_000_000m, numberOfDependents: 0);

            Assert.Equal(3_150_000m, result.InsuranceDeduction);   // 10.5% of 30M
            Assert.Equal(15_850_000m, result.TaxableIncome);       // 30M - 3.15M - 11M
            Assert.Equal(1_627_500m, result.TaxAmount);            // 250k + 500k + 877.5k
            Assert.Equal(3, result.TaxBracket);
        }

        [Fact]
        public void Dependents_reduce_taxable_income()
        {
            var result = _service.Calculate(30_000_000m, numberOfDependents: 2);

            Assert.Equal(8_800_000m, result.DependentDeduction);
            Assert.Equal(7_050_000m, result.TaxableIncome);
            Assert.Equal(455_000m, result.TaxAmount);              // 250k + 10% of 2.05M
            Assert.Equal(2, result.TaxBracket);
        }

        [Fact]
        public void Insurance_is_capped_for_high_salaries()
        {
            var result = _service.Calculate(100_000_000m, numberOfDependents: 0);

            Assert.Equal(4_914_000m, result.InsuranceDeduction);   // 10.5% of the 46.8M cap
            Assert.Equal(84_086_000m, result.TaxableIncome);
            Assert.Equal(ProgressiveTax(84_086_000m), result.TaxAmount);
            Assert.Equal(7, result.TaxBracket);
        }

        [Fact]
        public void Income_below_deductions_owes_no_tax()
        {
            var result = _service.Calculate(10_000_000m, numberOfDependents: 0);

            Assert.Equal(0m, result.TaxableIncome);
            Assert.Equal(0m, result.TaxAmount);
        }

        [Fact]
        public void Precomputed_insurance_and_configured_deductions_are_used_as_given()
        {
            var result = _service.Calculate(40_000_000m, numberOfDependents: 1, isInsuranceApplicable: true,
                insuranceAmount: 2_000_000m, personalDeduction: 15_500_000m, dependentDeduction: 6_200_000m);

            Assert.Equal(2_000_000m, result.InsuranceDeduction);
            Assert.Equal(16_300_000m, result.TaxableIncome);
            Assert.Equal(ProgressiveTax(16_300_000m), result.TaxAmount);
        }

        [Fact]
        public void No_insurance_when_not_applicable()
        {
            var result = _service.Calculate(20_000_000m, numberOfDependents: 0, isInsuranceApplicable: false);

            Assert.Equal(0m, result.InsuranceDeduction);
            Assert.Equal(9_000_000m, result.TaxableIncome);
        }

        // A wrong quick-deduction constant shows up as a jump at a bracket edge
        [Theory]
        [InlineData(1_000_000)]
        [InlineData(5_000_000)]
        [InlineData(5_000_001)]
        [InlineData(10_000_000)]
        [InlineData(18_000_000)]
        [InlineData(32_000_000)]
        [InlineData(52_000_000)]
        [InlineData(80_000_000)]
        [InlineData(80_000_001)]
        [InlineData(150_000_000)]
        public void Quick_formula_matches_progressive_calculation(int taxableIncome)
        {
            // No insurance and no deductions, so the taxable income is exactly the gross
            var result = _service.Calculate(taxableIncome, numberOfDependents: 0, isInsuranceApplicable: false,
                insuranceAmount: null, personalDeduction: 0, dependentDeduction: 0);

            Assert.Equal(Math.Round(ProgressiveTax(taxableIncome), 0), result.TaxAmount);
        }
    }
}

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;
using Google.OrTools;
using Google.OrTools.Sat;

namespace OutSystems.NssTargetGradingSolver
{
    class MixtureTargetGradingSolver
    {

        private readonly MaterialData[] materials;
        private readonly decimal[] targetPassPcts;
        private readonly int decimalPrecision;
        private readonly int numberOfSieves;
        // Multiplier used to convert percentage (0 <= pct <= 1) to an integer.
        // This is 100 * 10^decimalPrecision
        private readonly int pctToIntegerMultiplier;

        public MixtureTargetGradingSolver(MaterialData[] materials, decimal[] targetPassPcts, int decimalPrecision)
        {
            this.materials = materials;
            this.targetPassPcts = targetPassPcts;
            this.decimalPrecision = decimalPrecision;
            this.numberOfSieves = materials[0].PassPcts.Length;
            this.pctToIntegerMultiplier = (int)Math.Pow(10, decimalPrecision);
        }

        public (MaterialData material, decimal volumePercentage)[] Solve()
        {
            // Creates the model.
            // [START model]
            CpModel model = new CpModel();
            // [END model]
            // [START variables]
            // Creates the volume percentage variables.
            // These are the volume percentages for all materials.
            var volumePctVarMap = new Dictionary<string, IntVar>();
            var volumePctVars = new List<IntVar>();

            if (materials.Length == 0) {
                throw new System.InvalidOperationException("Empty materials list");
            }

            foreach (var material in materials)
            {
                IntVar variable = model.NewIntVar(0, 100 * this.pctToIntegerMultiplier, "v%" + material.Id);
                volumePctVarMap.Add(material.Id, variable);
                volumePctVars.Add(variable);
            }
            // Creates the pass percentage variables for the mixture.
            var mixturePassPctVarMap = new Dictionary<string, IntVar>();
            var mixturePassPctVars = new List<IntVar>();
            for (int sieveIndex = 0; sieveIndex < numberOfSieves; sieveIndex++)
            {
                IntVar variable = model.NewIntVar(0, 100 * this.pctToIntegerMultiplier * 100 * this.pctToIntegerMultiplier, "Y" + sieveIndex);
                mixturePassPctVarMap.Add("Y" + sieveIndex, variable);
                mixturePassPctVars.Add(variable);
            }
            // Creates the pass percentage delta variables
            var passPctDeltaVarMap = new Dictionary<string, IntVar>();
            var passPctDeltaVars = new List<IntVar>();
            var passPctDeltaAbsVarMap = new Dictionary<string, IntVar>();
            var passPctDeltaAbsVars = new List<IntVar>();
            for (int sieveIndex = 0; sieveIndex < numberOfSieves; sieveIndex++)
            {
                IntVar variable = model.NewIntVar(-1 * 100 * this.pctToIntegerMultiplier * 100 * this.pctToIntegerMultiplier, 100 * this.pctToIntegerMultiplier * 100 * this.pctToIntegerMultiplier, "Ydelta" + sieveIndex);
                IntVar absVariable = model.NewIntVar(0, 100 * this.pctToIntegerMultiplier * 100 * this.pctToIntegerMultiplier, "YdeltaAbs" + sieveIndex);
                passPctDeltaVarMap.Add("Ydelta" + sieveIndex, variable);
                passPctDeltaVars.Add(variable);
                passPctDeltaAbsVarMap.Add("YdeltaAbs" + sieveIndex, absVariable);
                passPctDeltaAbsVars.Add(absVariable);
            }
            // [END variables]
            // [START constraints]
            // Add the mixture pass percentage constraints (Y1 = v%1*Y11 + v%2*Y12 + ... +  v%n*Y1n).
            for (int sieveIndex = 0; sieveIndex < numberOfSieves; sieveIndex++)
            {
                int[] passPcts = this.materials.Select(material => (int)Math.Round(material.PassPcts[sieveIndex] * pctToIntegerMultiplier)).ToArray();
                IntVar[] variables = this.materials.Select(material => volumePctVarMap[material.Id]).ToArray();

                model.Add(WeightedSum(passPcts, variables) == mixturePassPctVars[sieveIndex]);
            }
            // Add the volume percentages should add up to 100% constraint.
            model.Add(new SumArray(volumePctVars.ToArray()) == 100 * pctToIntegerMultiplier);

            if (targetPassPcts.Length == 0)
            {
                throw new System.InvalidOperationException("Empty targetPassPcts list");
            }

            // Add the delta variable constraints.
            for (int sieveIndex = 0; sieveIndex < numberOfSieves; sieveIndex++)
            {
                int targetY = (int)Math.Round(targetPassPcts[sieveIndex] * pctToIntegerMultiplier * pctToIntegerMultiplier * pctToIntegerMultiplier);
                model.Add(passPctDeltaVars[sieveIndex] == mixturePassPctVars[sieveIndex] - targetY);
                //model.Add(passPctDeltaVars[sieveIndex] == mixturePassPctVars[sieveIndex] - 800);
                model.AddAbsEquality(passPctDeltaVars[sieveIndex], passPctDeltaAbsVars[sieveIndex]);
            }

            // [END constraints]

            model.Minimize(new SumArray(passPctDeltaAbsVars));
            //model.Minimize(new SumArray(mixturePassPctVars));

            // [START solve]
            CpSolver solver = new CpSolver();
            CpSolverStatus status = solver.Solve(model);
            // [END solve]

            (MaterialData material, decimal volumePercentage)[] result = new (MaterialData material, decimal volumePercentage)[materials.Length];
            
            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                for (int i = 0; i < result.Length; i++)
                {
                    long volumePctInteger = solver.Value(volumePctVars[i]);
                    decimal volumePct = volumePctInteger / pctToIntegerMultiplier;
                    var resultItem = (materials[i], volumePct);
                    result[i] = resultItem;
                }

                foreach (var mixturePassPctVar in mixturePassPctVars)
                {
                    Console.WriteLine(mixturePassPctVar.Name() + " = " + solver.Value(mixturePassPctVar));
                }
                foreach (var volumePct in volumePctVars)
                {
                    Console.WriteLine(volumePct.Name() + " = " + solver.Value(volumePct));
                }
                foreach (var delta in passPctDeltaAbsVars)
                {
                    Console.WriteLine(delta.Name() + " = " + solver.Value(delta));
                }
            }
            else
            {
                Console.WriteLine("Status was " + status + ", which is not feasible.");
            }
            
            return result;
        }

        private LinearExpr WeightedSum(int[] materialPassPcts, IntVar[] variables)
        {
            if (materialPassPcts.Length != variables.Length) throw new ArgumentException("Number of weights should be the same as number of variables");
            if (variables.Length == 0) throw new ArgumentException("At least 1 variable should be provided");

            LinearExpr result = materialPassPcts[0] * variables[0];
            for (int i = 1; i < variables.Length; i++)
            {
                result = result + materialPassPcts[i] * variables[i];
            }

            return result;
        }

    }

    class MaterialData
    {

        private readonly string id;
        private readonly string name;
        private readonly decimal[] passPcts;

        public MaterialData(string id, string name, params decimal[] passPcts)
        {
            this.id = id;
            this.name = name;
            this.passPcts = passPcts;
        }

        public string Id => id;

        public string Name => name;

        public decimal[] PassPcts => passPcts;
    }

}

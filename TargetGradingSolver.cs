using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;
using Google.OrTools;
using Google.OrTools.Sat;

namespace Decock.Steven.TargetGradingSolver
{

    public class TargetGradingSolver {

        static void Main()
        {
            // We are looking for the optimal mix of two materials that have different properties.
            // Each material is made up of particles of different sizes, think two types of gravel 
            // that have different size rocks in them.
            // We measure the properties of the material by measuring the volume of material that 
            // can pass through two different sieves:
            // Sieve 1, for example, has 10mm holes in it.
            // Sieve 2, for example, has 2mm holes in it.

            // For each material we measure the volume (in %) of the material that can pass through each sieve.
            // Material 1:
            //  - 70% passes through sieve 1
            //  - 30% also passes through sieve 2
            // So that means:
            //  - 30% of the volume of material 1 are particles > 10 mm
            //  - 40% of the volume of material 1 are particles <= 10 mm and > 2 mm
            //  - 30% of the volume of material 1 are particles < 2 mm
            // Material 2 has different properties:
            //  - 90% passes through sieve 1
            //  - 75% also passes through sieve 2

            // Our goal is to find a mix of both materials that comes as close as possible to 
            // our target particle distribution.
            // For this example, let's say we want:
            // Target mix:
            //  - 80% passes through sieve 1
            //  - 50% also passes through sieve 2

            // Se would like to find out how to mix both materials (what % of each in the mix) so that 
            // the mix has the desired properties?

            // Creating the model.
            // [START model]
            CpModel model = new CpModel();
            // [END model]

            // [START variables]
            // These are the variables we are actually interested in.
            // The volume percentages of each material in the mix. How much (in %) of each material should we put in the mix? 
            IntVar volPctMaterial1 = model.NewIntVar(0, 10000, "v%1");
            IntVar volPctMaterial2 = model.NewIntVar(0, 10000, "v%2");

            // The rest of the variables are intermediate variables.  
            // First we have the percentages of the mix that pass through both sieves.  
            // These should be as close as possible to our target (80/50 in our example).
            IntVar sieve1PassPctForMix = model.NewIntVar(0, 100000000, "Y1");
            IntVar sieve2PassPctForMix = model.NewIntVar(0, 100000000, "Y2");

            // These variables will contain the difference between the actual and target pass percentages for the mix.
            // For example: if the target is 80% through sieve 1, but our solution finds a mix where 75% passes through sieve 1, 
            // then the passPctDeltaForSieve1 is 5% (80% - 75%).
            IntVar passPctDeltaSieve1 = model.NewIntVar(-100000000, 100000000, "passPctDeltaSieve1");
            IntVar passPctDeltaSieve2 = model.NewIntVar(-100000000, 100000000, "passPctDeltaSieve2");

            // These variables are the ABS() (positive) values of our delta variables.
            // We will optimize for the lowest sum of these ABS() delta variables, so that our mix comes as close as 
            // possible to our desired pass percentages.
            IntVar passPctDeltaAbsSieve1 = model.NewIntVar(0, 100000000, "passPctDeltaAbsSieve1");
            IntVar passPctDeltaAbsSieve2 = model.NewIntVar(0, 100000000, "passPctDeltaAbsSieve2");
            // [END variables]

            // [START constraints]
            // Add the mixture pass percentage constraints (Y1 = v%1*Y11 + v%2*Y12 + ... +  v%n*Y1n).
            // The formula for the amount of the mix that passes through each sieve is the weighted sum of the pass 
            // percentages of the materials in the mix.
            // For example: if the mix is 50% material 1, 50% material 2, and 70% of material 1 passes through sieve 1, 
            // while 90% of material 2 passes through sieve 1, 
            // then 80% of the mix would pass through sieve 1 (= 50%*70% + 50%*90%).
            model.Add(sieve1PassPctForMix == 7000 * volPctMaterial1 + 9000 * volPctMaterial2);
            model.Add(sieve2PassPctForMix ==  3000 * volPctMaterial1 + 7500 * volPctMaterial2);

            // The volume percentages should add up to 100% constraint.
            // The mix can for example be 40% material 1, 60% material 2: 40 + 60 = 100
            model.Add(volPctMaterial1 + volPctMaterial2 == 10000);

            // We calculate the difference between our target pass percentages and our target
            model.Add(passPctDeltaSieve1 == sieve1PassPctForMix - 80000000);
            model.Add(passPctDeltaSieve2 == sieve2PassPctForMix - 50000000);

            // We now calculate the absolute values of these differences, so we can minimize their sum.
            model.AddAbsEquality(passPctDeltaAbsSieve1, passPctDeltaSieve1);
            model.AddAbsEquality(passPctDeltaAbsSieve2, passPctDeltaSieve2);

            // !!!!!!!!!!!!!!!!
            // !!!! ISSUE !!!!!

            // Uncommenting the line below makes the solution INFEASIBLE.  Why?
            model.Add(volPctMaterial2 == 3000); // 30% of the mix needs to be material 2

            // Observation #1:
            // Uncommenting the above line does NOT make the solution INFEASIBLE if we remove the 
            // two AddAbsEquality constraints.  Why?
            // Why does adding a variable that is the ABS() of another value constrain the solution?
            // There always is a value that is the ABS() of another value.  We are not mentioning
            // passPctDeltaAbsSieve1 and passPctDeltaAbsSieve2 in any other constraints, so the algorithm 
            // should always be able to find a value for them (the ABS() of passPctDeltaSieve1 and passPctDeltaSieve2)

            // Observation #2:
            // Changing the value 3000 (30% ) in the above constraint to 5000 (50%) makes the solution FEASIBLE.  Why?
            // Both should be feasible.

            // !!!!!!!!!!!!!!!!

            // [END constraints]
            // We minimize the sum of the absolute difference between the actual and target pass percentages for the mix.
            model.Minimize(new SumArray(new IntVar[] {
                passPctDeltaAbsSieve1,
                passPctDeltaAbsSieve2, 
            }));

            // [START solve]
            CpSolver solver = new CpSolver();
            CpSolverStatus status = solver.Solve(model);
            // [END solve]

            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                Console.WriteLine("ObjectiveValue: " + solver.ObjectiveValue);

                Console.WriteLine("volPctMaterial1 = " + solver.Value(volPctMaterial1));
                Console.WriteLine("volPctMaterial2 = " + solver.Value(volPctMaterial2));
                Console.WriteLine("sieve1PassPctForMix (Y1) = " + solver.Value(sieve1PassPctForMix));
                Console.WriteLine("sieve2PassPctForMix (Y2) = " + solver.Value(sieve2PassPctForMix));
                Console.WriteLine("passPctDeltaSieve1 = " + solver.Value(passPctDeltaSieve1));
                Console.WriteLine("passPctDeltaSieve2 = " + solver.Value(passPctDeltaSieve2));
                Console.WriteLine("passPctDeltaAbsSieve1 = " + solver.Value(passPctDeltaAbsSieve1));
                Console.WriteLine("passPctDeltaAbsSieve2 = " + solver.Value(passPctDeltaAbsSieve2));
            }
            else
            {
                Console.WriteLine("Status was " + status + ", which is not feasible.  Response stats: " + solver.ResponseStats());
            }

            Console.ReadLine();
        }

    }

} // OutSystems.NssTargetGradingSolver


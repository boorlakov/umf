using Fengine.Backend.Fem.Mesh;
using Fengine.Backend.Integration;
using Fengine.Backend.LinAlg;
using Fengine.Backend.LinAlg.Matrix;
using Fengine.Backend.LinAlg.SlaeSolver;
using Fengine.Backend.Models;
using Sprache.Calc;

namespace Fengine.Backend.Fem.Solver;

public class FemSolverWithSimpleIteration : IFemSolver
{
    private readonly IIntegrator _integrator;
    private readonly ISlaeSolver _slaeSolver;
    private readonly IMatrix _matrix;

    public FemSolverWithSimpleIteration(
        ISlaeSolver slaeSolver,
        IIntegrator integrator,
        IMatrix matrix
    )
    {
        _slaeSolver = slaeSolver;
        _integrator = integrator;
        _matrix = matrix;
    }

    public Statistics Solve(
        IMesh mesh,
        InputFuncs inputFuncs,
        Area area,
        BoundaryConditions boundaryConditions,
        Accuracy accuracy
    )
    {
        var iter = 0;
        var coef = 0.0;
        var initApprox = new double[mesh.Nodes.Length];

        var relaxRatio = accuracy.RelaxRatio;
        var slae = new Slae1DEllipticLinearFNonLinear();

        do
        {
            slae.ResVec.AsSpan().CopyTo(initApprox);
            slae = new Slae1DEllipticLinearFNonLinear(
                mesh,
                inputFuncs,
                initApprox,
                _slaeSolver,
                _integrator,
                _matrix
            );
            ApplyBoundaryConditions(slae.Matrix, slae.RhsVec, area, boundaryConditions);
            slae.Solve(accuracy);

            if (accuracy.AutoRelax)
            {
                relaxRatio = RelaxRatio(slae.ResVec,
                    mesh,
                    inputFuncs,
                    initApprox,
                    accuracy,
                    area,
                    boundaryConditions
                );
            }

            coef = relaxRatio;
            initApprox = UpdateApprox(slae.ResVec, initApprox, relaxRatio);
            iter++;

            Console.Write($"\r[INFO] RelRes = {LinAlg.Utils.RelResidual(slae):G10} | Iter: {iter}");
        } while (iter < accuracy.MaxIter &&
                 LinAlg.Utils.RelResidual(slae) > accuracy.Eps &&
                 !LinAlg.Utils.CheckIsStagnate(slae.ResVec, initApprox, accuracy.Delta));

        var funcCalc = new XtensibleCalculator();
        var uStar = funcCalc.ParseFunction(inputFuncs.UStar).Compile();

        var absError = new double[slae.ResVec.Length];
        var u = new double[slae.ResVec.Length];

        for (var i = 0; i < slae.ResVec.Length; i++)
        {
            u[i] = uStar(Utils.MakeDict1D(mesh.Nodes[i].Coordinates["x"]));
            absError[i] = u[i] - slae.ResVec[i];
        }

        var error = GeneralOperations.Norm(absError) / GeneralOperations.Norm(u);

        var stat = new Statistics
        {
            Iterations = iter,
            Residual = LinAlg.Utils.RelResidual(slae),
            Error = error,
            Values = slae.ResVec,
            RelaxRatio = coef
        };

        return stat;
    }

    private double RelaxRatio(
        double[] resVec,
        IMesh cartesian1DMesh,
        InputFuncs inputFuncs,
        double[] prevResVec,
        Accuracy accuracy,
        Area area,
        BoundaryConditions boundaryConditions
    )
    {
        var gold = (Math.Sqrt(5) - 1.0) / 2.0;
        var left = .0;
        var right = 1.0;
        var xLeft = 1 - gold;
        var xRight = gold;

        var fLeft = ResidualFunc(
            resVec,
            cartesian1DMesh,
            inputFuncs,
            xLeft,
            prevResVec,
            area,
            boundaryConditions
        );
        var fRight = ResidualFunc(
            resVec,
            cartesian1DMesh,
            inputFuncs,
            xRight,
            prevResVec,
            area,
            boundaryConditions
        );

        while (Math.Abs(right - left) > accuracy.Eps)
        {
            if (fLeft > fRight)
            {
                left = xLeft;
                xLeft = xRight;
                fLeft = fRight;
                xRight = left + gold * (right - left);
                fRight = ResidualFunc(
                    resVec,
                    cartesian1DMesh,
                    inputFuncs,
                    xRight,
                    prevResVec,
                    area,
                    boundaryConditions
                );
            }
            else
            {
                right = xRight;
                xRight = xLeft;
                fRight = fLeft;
                xLeft = left + (1.0 - gold) * (right - left);
                fLeft = ResidualFunc(
                    resVec,
                    cartesian1DMesh,
                    inputFuncs,
                    xLeft,
                    prevResVec,
                    area,
                    boundaryConditions
                );
            }
        }

        return (left + right) / 2.0;
    }

    private double ResidualFunc(
        double[] resVec,
        IMesh cartesian1DMesh,
        InputFuncs inputFuncs,
        double x,
        double[] prevResVec,
        Area area,
        BoundaryConditions boundaryConditions
    )
    {
        var approx = new double[prevResVec.Length];

        for (var i = 0; i < approx.Length; i++)
        {
            approx[i] = x * resVec[i] + (1.0 - x) * prevResVec[i];
        }

        var slae = new Slae1DEllipticLinearFNonLinear(
            cartesian1DMesh,
            inputFuncs,
            approx,
            _slaeSolver,
            _integrator,
            _matrix
        );
        ApplyBoundaryConditions(slae.Matrix, slae.RhsVec, area, boundaryConditions);
        return LinAlg.Utils.RelResidual(slae.Matrix, approx, slae.RhsVec);
    }

    private static double[] UpdateApprox(
        double[] resVec,
        double[] approx,
        double relaxRatio
    )
    {
        var newApprox = new double[resVec.Length];

        for (var i = 0; i < resVec.Length; i++)
        {
            newApprox[i] = relaxRatio * resVec[i] + (1.0 - relaxRatio) * approx[i];
        }

        return newApprox;
    }

    private static void ApplyBoundaryConditions(
        IMatrix m,
        double[] rhs,
        Area area,
        BoundaryConditions boundaryConditions
    )
    {
        switch (boundaryConditions.Left)
        {
            case "First":
                m.Data["center"][0] = 1.0;
                m.Data["upper"][0] = 0.0;
                rhs[0] = Utils.EvalFunc(boundaryConditions.LeftFunc, area.LeftBorder);
                break;
            case "Second":
                rhs[0] += Utils.EvalFunc(boundaryConditions.LeftFunc, area.LeftBorder);
                break;
            case "Third":
                m.Data["center"][0] += boundaryConditions.Beta;
                rhs[0] += boundaryConditions.Beta * Utils.EvalFunc(boundaryConditions.LeftFunc, area.LeftBorder);
                break;
        }

        switch (boundaryConditions.Right)
        {
            case "First":
                m.Data["center"][^1] = 1.0;
                m.Data["lower"][^1] = 0.0;
                rhs[^1] = Utils.EvalFunc(boundaryConditions.RightFunc, area.RightBorder);
                break;
            case "Second":
                rhs[^1] += Utils.EvalFunc(boundaryConditions.RightFunc, area.RightBorder);
                break;
            case "Third":
                m.Data["center"][^1] += boundaryConditions.Beta;
                rhs[^1] += boundaryConditions.Beta * Utils.EvalFunc(boundaryConditions.RightFunc, area.RightBorder);
                break;
        }
    }
}
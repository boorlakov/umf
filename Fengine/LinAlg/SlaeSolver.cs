using Fengine.Fem;
using Fengine.Models;

namespace Fengine.LinAlg;

/// <summary>
///     Class for holding methods of solving systems of linear equations
/// </summary>
public class SlaeSolver
{
    public double[] Solve(Slae slae, Accuracy accuracy)
    {
        slae.ResVec = Iterate(slae.ResVec, slae.Matrix, 1.7, slae.RhsVec);
        var residual = RelResidual(slae.Matrix, slae.ResVec, slae.RhsVec);
        var iter = 1;
        var prevResVec = new double[slae.ResVec.Length];

        while (iter <= accuracy.MaxIter && accuracy.Eps < residual &&
               !CheckIsStagnate(prevResVec, slae.ResVec, accuracy.Delta))
        {
            slae.ResVec.AsSpan().CopyTo(prevResVec);
            slae.ResVec = Iterate(slae.ResVec, slae.Matrix, 1.0, slae.RhsVec);
            residual = RelResidual(slae.Matrix, slae.ResVec, slae.RhsVec);
            iter++;
        }

        return slae.ResVec;
    }

    /// <summary>
    ///     1 iteration of Gauss-Seidel iteration method of solving Ax = f
    /// </summary>
    /// <param name="x">Given approximation. x part in slae</param>
    /// <param name="matrix">Given weights. A part in slae</param>
    /// <param name="w">Relaxation parameter</param>
    /// <param name="f">Right part (f) of the slae</param>
    /// <returns>New approximation x</returns>
    public double[] Iterate(double[] x, Matrix matrix, double w, double[] f)
    {
        for (var i = 0; i < x.Length; i++)
        {
            var sum = GeneralOperations.Dot(i, matrix, x);
            x[i] += w * (f[i] - sum) / matrix.Center[i];
        }

        return x;
    }

    /// <summary>
    ///     Checks if iteration method is stagnating or not
    /// </summary>
    /// <param name="prevVec">Previous value of a vector</param>
    /// <param name="vec">Current value of a vector</param>
    /// <param name="delta">Tolerance parameter</param>
    /// <returns>True, if method is stagnating. Otherwise, false</returns>
    public bool CheckIsStagnate(double[] prevVec, double[] vec, double delta)
    {
        var difVec = new double[vec.Length];

        for (var i = 0; i < vec.Length; i++)
        {
            difVec[i] = prevVec[i] - vec[i];
        }

        return Math.Abs(GeneralOperations.Norm(difVec)) < delta;
    }

    /// <summary>
    ///     Relative residual (||f - Ax|| / ||f||) of slae Ax = f
    /// </summary>
    /// <param name="matrix">Given weights. A part in slae</param>
    /// <param name="x">Given approximation. x part in slae</param>
    /// <param name="f">Right part (f) of the slae</param>
    /// <returns>Relative residual value</returns>
    public double RelResidual(Matrix matrix, double[] x, double[] f)
    {
        var diff = new double[f.Length];

        var innerProd = GeneralOperations.MatMul(matrix, x);

        for (var i = 0; i < f.Length; i++)
        {
            diff[i] = f[i] - innerProd[i];
        }

        return GeneralOperations.Norm(diff) / GeneralOperations.Norm(f);
    }

    /// <summary>
    ///     Relative residual (||f - Ax|| / ||f||) of slae Ax = f
    /// </summary>
    /// <param name="slae">Given slae</param>
    /// <returns>Relative residual value</returns>
    public double RelResidual(Slae slae)
    {
        var diff = new double[slae.RhsVec.Length];

        var innerProd = GeneralOperations.MatMul(slae.Matrix, slae.ResVec);

        for (var i = 0; i < slae.RhsVec.Length; i++)
        {
            diff[i] = slae.RhsVec[i] - innerProd[i];
        }

        return GeneralOperations.Norm(diff) / GeneralOperations.Norm(slae.RhsVec);
    }
}
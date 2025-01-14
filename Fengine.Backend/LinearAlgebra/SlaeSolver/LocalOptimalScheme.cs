namespace Fengine.Backend.LinearAlgebra.SlaeSolver;

public class LocalOptimalScheme : ISlaeSolver
{
    public double[] Solve
    (
        Fem.Slae.ISlae slae,
        DataModels.Accuracy accuracy
    )
    {
        if (slae.Matrix.Decomposed)
        {
            throw new ArgumentException("Matrix must be not decomposed");
        }

        var precondMatrix = new Matrix.Sparse(slae.Matrix);
        precondMatrix.Factorize();

        var x = new double[slae.RhsVec.Length];
        slae.ResVec.AsSpan().CopyTo(x);

        // A x_{0}
        var matMulRes = GeneralOperations.MatrixMultiply(slae.Matrix, x);

        // f - A x_{0}
        for (var i = 0; i < slae.Matrix.Size; i++)
        {
            matMulRes[i] = slae.RhsVec[i] - matMulRes[i];
        }

        // r = L^{-1} (f - A x_{0}) === Lr = (f - A x_{0}) ==> Forward
        var r = ForwardPropagation(precondMatrix, matMulRes);

        // r = (r_{0}, r_{0})         
        var residual = GeneralOperations.Dot(r, r);

        // Need for checking stagnation 
        var residualNext = residual + 1.0;

        var absResidualDifference = Math.Abs(residual - residualNext);

        // z = U^{-1} r === Uz = r ==> Backward
        var z = BackwardPropagation(precondMatrix, r);

        // A z_{0}
        matMulRes = GeneralOperations.MatrixMultiply(slae.Matrix, z);

        // p_{0} = L^{-1} Az_{0} === L p_{0} = A z_{0} ==> Forward
        var p = ForwardPropagation(precondMatrix, matMulRes);

        // Current iteration
        var k = 1;
        Console.Write($"\r[INFO] Iter: {0}, Residual: {residual}, Stagnation value: {absResidualDifference}");

        for (; Math.Abs(residual) > accuracy.Eps && k < accuracy.MaxIter && absResidualDifference > accuracy.Delta; k++)
        {
            var pp = GeneralOperations.Dot(p, p);
            var alpha = GeneralOperations.Dot(p, r) / pp;

            absResidualDifference = Math.Abs(residual - residualNext);

            // Updating residual
            residualNext = residual;

            // We dont need to over-calculate scalar product, because we calculated at k = 0
            // r_{k} = - α^2 (p_{k-1}, p_{k-1}) 
            residual -= alpha * alpha * pp;
            Console.Write($"\r[INFO] Iter: {k}, Residual: {residual}, Stagnation value: {absResidualDifference}");

            // Updating to {k} 
            for (var i = 0; i < slae.Matrix.Size; i++)
            {
                x[i] += alpha * z[i];
                r[i] -= alpha * p[i];
            }

            // Go from right to left. Need to avoid finding reverse matrices
            var ur = BackwardPropagation(precondMatrix, r);
            matMulRes = GeneralOperations.MatrixMultiply(slae.Matrix, ur);

            var dotRhs = ForwardPropagation(precondMatrix, matMulRes);

            // β = - (p_{k-1}, L^{-1} A U^{-1} r_{k}) / (p_{k-1}, p_{k-1}) 
            var beta = -GeneralOperations.Dot(p, dotRhs) / pp;

            // Updating to {k} 
            for (var i = 0; i < slae.Matrix.Size; i++)
            {
                // z_{k} = U^{-1} r + β z_{k-1} 
                z[i] = ur[i] + beta * z[i];

                // p_{k} = L^{-1} A U^{-1} r_{k} + β p_{k-1}
                p[i] = dotRhs[i] + beta * p[i];
            }
        }

        x.AsSpan().CopyTo(slae.ResVec);
        return x;
    }

    public static double[] Solve
    (
        Matrix.Sparse matrix,
        double[] rhs,
        DataModels.Accuracy accuracy
    )
    {
        if (matrix.Decomposed)
        {
            throw new ArgumentException("Matrix must be not decomposed");
        }

        var precondMatrix = new Matrix.Sparse(matrix);
        precondMatrix.Factorize();

        var x = new double[rhs.Length];

        // A x_{0}
        var matMulRes = GeneralOperations.MatrixMultiply(matrix, x);

        // f - A x_{0}
        for (var i = 0; i < matrix.Size; i++)
        {
            matMulRes[i] = rhs[i] - matMulRes[i];
        }

        // r = L^{-1} (f - A x_{0}) === Lr = (f - A x_{0}) ==> Forward
        var r = ForwardPropagation(precondMatrix, matMulRes);

        // r = (r_{0}, r_{0})         
        var residual = GeneralOperations.Dot(r, r);

        // Need for checking stagnation 
        var residualNext = residual + 1.0;

        var absResidualDifference = Math.Abs(residual - residualNext);

        // z = U^{-1} r === Uz = r ==> Backward
        var z = BackwardPropagation(precondMatrix, r);

        // A z_{0}
        matMulRes = GeneralOperations.MatrixMultiply(matrix, z);

        // p_{0} = L^{-1} Az_{0} === L p_{0} = A z_{0} ==> Forward
        var p = ForwardPropagation(precondMatrix, matMulRes);

        // Current iteration
        var k = 1;

        for (; residual > accuracy.Eps && k < accuracy.MaxIter && absResidualDifference > accuracy.Delta; k++)
        {
            var pp = GeneralOperations.Dot(p, p);
            var alpha = GeneralOperations.Dot(p, r) / pp;

            absResidualDifference = Math.Abs(residual - residualNext);

            // Updating residual
            residualNext = residual;

            // We dont need to over-calculate scalar product, because we calculated at k = 0
            // r_{k} = - α^2 (p_{k-1}, p_{k-1}) 
            residual -= alpha * alpha * pp;
            Console.Write(
                $"\r[INFO] Iter: {k,3}, Residual: {residual,20}, Stagnation value: {absResidualDifference,20}");

            // Updating to {k} 
            for (var i = 0; i < matrix.Size; i++)
            {
                x[i] += alpha * z[i];
                r[i] -= alpha * p[i];
            }

            // Go from right to left. Need to avoid finding reverse matrices
            var ur = BackwardPropagation(precondMatrix, r);
            matMulRes = GeneralOperations.MatrixMultiply(matrix, ur);

            var dotRhs = ForwardPropagation(precondMatrix, matMulRes);

            // β = - (p_{k-1}, L^{-1} A U^{-1} r_{k}) / (p_{k-1}, p_{k-1}) 
            var beta = -GeneralOperations.Dot(p, dotRhs) / pp;

            // Updating to {k} 
            for (var i = 0; i < matrix.Size; i++)
            {
                // z_{k} = U^{-1} r + β z_{k-1} 
                z[i] = ur[i] + beta * z[i];

                // p_{k} = L^{-1} A U^{-1} r_{k} + β p_{k-1}
                p[i] = dotRhs[i] + beta * p[i];
            }
        }

        return x;
    }

    /// <summary>
    /// Forward solution for Lx = b. Made for avoiding L^{-1}
    /// </summary>
    /// <param name="l"> Decomposed matrix in sparse format </param>
    /// <param name="b"> RHS vector </param>
    /// <returns> Solution for lower triangular part of matrix A</returns>
    /// <exception cref="Matrix.Exception.NotDecomposedException"> Required decomposed matrix </exception>
    private static double[] ForwardPropagation(Matrix.IMatrix l, double[] b)
    {
        if (!l.Decomposed)
        {
            throw new Matrix.Exception.NotDecomposedException();
        }

        var y = new double[b.Length];
        b.AsSpan().CopyTo(y);

        for (var i = 0; i < y.Length; i++)
        {
            var sum = 0.0;

            for (var j = l.Profile["ig"][i]; j < l.Profile["ig"][i + 1]; j++)
            {
                sum += l.Data["ggl"][j] * y[l.Profile["jg"][j]];
            }

            y[i] -= sum;
            y[i] /= l.Data["di"][i];
        }

        return y;
    }

    /// <summary>
    /// Backward solution for Ux = y. Made for avoiding U^{-1}
    /// </summary>
    /// <param name="u"> Decomposed matrix in sparse format </param>
    /// <param name="y"> RHS vector </param>
    /// <returns> Solution for upper triangular part of matrix A</returns>
    /// <exception cref="Matrix.Exception.NotDecomposedException"> Required decomposed matrix </exception>
    private static double[] BackwardPropagation(Matrix.IMatrix u, double[] y)
    {
        if (!u.Decomposed)
        {
            throw new Matrix.Exception.NotDecomposedException();
        }

        var res = new double[y.Length];
        y.AsSpan().CopyTo(res);

        for (var i = u.Size - 1; i >= 0; i--)
        {
            res[i] /= u.Data["di"][i];

            for (var j = u.Profile["ig"][i]; j < u.Profile["ig"][i + 1]; j++)
            {
                res[u.Profile["jg"][j]] -= u.Data["ggu"][j] * res[i];
            }
        }

        return res;
    }
}
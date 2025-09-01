using ChessChallenge.API;
using System;
using System.Collections.Generic;

public static class PrecomputedEvalData
{
    public static readonly ulong[] WhitePassedPawnMask =
    {
        217020518514230016,
        506381209866536704,
        1012762419733073408,
        2025524839466146816,
        4051049678932293632,
        8102099357864587264,
        16204198715729174528,
        13889313184910721024,
        217020518514229248,
        506381209866534912,
        1012762419733069824,
        2025524839466139648,
        4051049678932279296,
        8102099357864558592,
        16204198715729117184,
        13889313184910671872,
        217020518514032640,
        506381209866076160,
        1012762419732152320,
        2025524839464304640,
        4051049678928609280,
        8102099357857218560,
        16204198715714437120,
        13889313184898088960,
        217020518463700992,
        506381209748635648,
        1012762419497271296,
        2025524838994542592,
        4051049677989085184,
        8102099355978170368,
        16204198711956340736,
        13889313181676863488,
        217020505578799104,
        506381179683864576,
        1012762359367729152,
        2025524718735458304,
        4051049437470916608,
        8102098874941833216,
        16204197749883666432,
        13889312357043142656,
        217017207043915776,
        506373483102470144,
        1012746966204940288,
        2025493932409880576,
        4050987864819761152,
        8101975729639522304,
        16203951459279044608,
        13889101250810609664,
        216172782113783808,
        504403158265495552,
        1008806316530991104,
        2017612633061982208,
        4035225266123964416,
        8070450532247928832,
        16140901064495857664,
        13835058055282163712,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
    };

    public static readonly ulong[] BlackPassedPawnMask =
    {
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        3,
        7,
        14,
        28,
        56,
        112,
        224,
        192,
        771,
        1799,
        3598,
        7196,
        14392,
        28784,
        57568,
        49344,
        197379,
        460551,
        921102,
        1842204,
        3684408,
        7368816,
        14737632,
        12632256,
        50529027,
        117901063,
        235802126,
        471604252,
        943208504,
        1886417008,
        3772834016,
        3233857728,
        12935430915,
        30182672135,
        60365344270,
        120730688540,
        241461377080,
        482922754160,
        965845508320,
        827867578560,
        3311470314243,
        7726764066567,
        15453528133134,
        30907056266268,
        61814112532536,
        123628225065072,
        247256450130144,
        211934100111552,
        847736400446211,
        1978051601041159,
        3956103202082318,
        7912206404164636,
        15824412808329272,
        31648825616658544,
        63297651233317088,
        54255129628557504,

    };

    public static readonly ulong[] AdjacentFilesMask =
    {
        144680345676153346,
        361700864190383365,
        723401728380766730,
        1446803456761533460,
        2893606913523066920,
        5787213827046133840,
        11574427654092267680,
        4629771061636907072,
    };
    
    public static readonly int[][] PawnShieldSquaresWhite;
    public static readonly int[][] PawnShieldSquaresBlack;

    static PrecomputedEvalData()
    {
        PawnShieldSquaresWhite = new int[64][];
        PawnShieldSquaresBlack = new int[64][];
        for (int squareIndex = 0; squareIndex < 64; squareIndex++)
        {
            CreatePawnShieldSquare(squareIndex);
        }
        
        OrthogonalDistance = new int[64, 64];
        CentreManhattanDistance = new int[64];

        InitializeDistances();
    }

    private static void CreatePawnShieldSquare(int squareIndex)
    {
        List<int> shieldIndicesWhite = new();
        List<int> shieldIndicesBlack = new();
        Square coord = new Square(squareIndex);
        int rank = coord.Rank;
        int file = Math.Clamp(coord.File, 1, 6);

        for (int fileOffset = -1; fileOffset <= 1; fileOffset++)
        {
            AddIfValid(new Square(file + fileOffset, rank + 1), shieldIndicesWhite);
            AddIfValid(new Square(file + fileOffset, rank - 1), shieldIndicesBlack);
        }

        for (int fileOffset = -1; fileOffset <= 1; fileOffset++)
        {
            AddIfValid(new Square(file + fileOffset, rank + 2), shieldIndicesWhite);
            AddIfValid(new Square(file + fileOffset, rank - 2), shieldIndicesBlack);
        }

        PawnShieldSquaresWhite[squareIndex] = shieldIndicesWhite.ToArray();
        PawnShieldSquaresBlack[squareIndex] = shieldIndicesBlack.ToArray();

        void AddIfValid(Square square, List<int> list)
        {
            if (square.File >= 0 && square.File < 8 && square.Rank >= 0 && square.Rank < 8)
            {
                list.Add(square.Index);
            }
        }
    }
    
    
    public static int[,] OrthogonalDistance;
    public static int[] CentreManhattanDistance;

    private static void InitializeDistances()
    {
        for (int squareA = 0; squareA < 64; squareA++)
        {
            Square coordA = new Square(squareA);
            int fileDstFromCentre = Math.Max(3 - coordA.File, coordA.File - 4);
            int rankDstFromCentre = Math.Max(3 - coordA.Rank, coordA.Rank - 4);
            CentreManhattanDistance[squareA] = fileDstFromCentre + rankDstFromCentre;

            for (int squareB = 0; squareB < 64; squareB++)
            {
                Square coordB = new Square(squareB);
                int rankDistance = Math.Abs(coordA.Rank - coordB.Rank);
                int fileDistance = Math.Abs(coordA.File - coordB.File);
                OrthogonalDistance[squareA, squareB] = fileDistance + rankDistance;
            }
        }
    }
}
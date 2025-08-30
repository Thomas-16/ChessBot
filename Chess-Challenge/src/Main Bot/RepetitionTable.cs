
using System;
using System.Linq;
using ChessChallenge.API;

public class RepetitionTable
{
    ulong[] hashes;
    int[] startIndices;
    int count;

    public RepetitionTable()
    {
        hashes = new ulong[256];
        startIndices = new int[hashes.Length + 1];
    }

    public void Init(Board board)
    {
        hashes = new ulong[256];
        startIndices = new int[hashes.Length + 1];
        
        ulong[] initialHashes = board.GameRepetitionHistory.Reverse().ToArray();
        count = initialHashes.Length;

        for (int i = 0; i < initialHashes.Length; i++)
        {
            hashes[i] = initialHashes[i];
        }
    
        // Set all start indices to count (current position)
        // This prevents checking historical positions for repetitions
        for (int i = 0; i <= count; i++)
        {
            startIndices[i] = count;
        }
    }


    public void Push(ulong hash, bool reset)
    {
        // Check bounds just in case
        if (count < hashes.Length)
        {
            hashes[count] = hash;
            startIndices[count + 1] = reset ? count : startIndices[count];
        }
        count++;
    }

    public void TryPop()
    {
        count = Math.Max(0, count - 1);
    }

    public bool Contains(ulong h)
    {
        int s = startIndices[count];
        // up to count-1 so that curr position is not counted
        for (int i = s; i < count - 1; i++)
        {
            if (hashes[i] == h)
            {
                return true;
            }
        }
        return false;
    }
}
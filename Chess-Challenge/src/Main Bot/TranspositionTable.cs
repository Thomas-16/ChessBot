using ChessChallenge.API;

public enum NodeType
{
    Exact,
    LowerBound,
    UpperBound
}

public struct TTEntry
{
    public ulong zobristKey;
    public int value;
    public int depth;
    public NodeType nodeType;
    public Move bestMove;
    
    public TTEntry(ulong zobristKey, int value, int depth, NodeType nodeType, Move bestMove)
    {
        this.zobristKey = zobristKey;
        this.value = value;
        this.depth = depth;
        this.nodeType = nodeType;
        this.bestMove = bestMove;
    }
}

public class TranspositionTable
{
    readonly TTEntry[] entries;
    readonly int size;
    private int count;
    
    public TranspositionTable(int sizeMB = 16)
    {
        size = sizeMB * 1024 * 1024 / 32;
        entries = new TTEntry[size];
        count = 0;
    }
    
    public bool TryGetEntry(ulong zobristKey, out TTEntry entry)
    {
        int index = (int)(zobristKey % (ulong)size);
        entry = entries[index];
        return entry.zobristKey == zobristKey;
    }
    
    public void StoreEntry(ulong zobristKey, int value, int depth, NodeType nodeType, Move bestMove)
    {
        int index = (int)(zobristKey % (ulong)size);
        
        // Only increment count if this is a new entry (not a replacement)
        if (entries[index].zobristKey == 0)
            count++;
        
        entries[index] = new TTEntry(zobristKey, value, depth, nodeType, bestMove);
    }
    
    public bool ProbeEntry(ulong zobristKey, int depth, int alpha, int beta, out int value, out Move bestMove)
    {
        value = 0;
        bestMove = Move.NullMove;
        
        if (TryGetEntry(zobristKey, out TTEntry entry) && entry.depth >= depth)
        {
            bestMove = entry.bestMove;
            
            switch (entry.nodeType)
            {
                case NodeType.Exact:
                    value = entry.value;
                    return true;
                case NodeType.LowerBound:
                    if (entry.value >= beta)
                    {
                        value = entry.value;
                        return true;
                    }
                    break;
                case NodeType.UpperBound:
                    if (entry.value <= alpha)
                    {
                        value = entry.value;
                        return true;
                    }
                    break;
            }
        }
        
        return false;
    }

    public int GetCount()
    {
        return count;
    }
}
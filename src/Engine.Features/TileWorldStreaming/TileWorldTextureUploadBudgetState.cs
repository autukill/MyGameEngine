namespace GameEngine.Features.TileWorldStreaming;

internal struct TileWorldTextureUploadBudgetState
{
    private readonly int _maximumTextures;
    private readonly long _maximumBytes;

    public TileWorldTextureUploadBudgetState(TileWorldTextureUploadBudget budget)
    {
        _maximumTextures = budget.MaximumTexturesPerUpdate;
        _maximumBytes = budget.MaximumBytesPerUpdate;
    }

    private TileWorldTextureUploadBudgetState(int maximumTextures, long maximumBytes)
    {
        _maximumTextures = maximumTextures;
        _maximumBytes = maximumBytes;
    }

    public static TileWorldTextureUploadBudgetState Unlimited => new(int.MaxValue, long.MaxValue);

    public int TexturesUploaded { get; private set; }
    public long BytesUploaded { get; private set; }

    public bool TryReserve(long bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (TexturesUploaded >= _maximumTextures) return false;
        if (TexturesUploaded > 0 && bytes > _maximumBytes - BytesUploaded) return false;
        TexturesUploaded++;
        BytesUploaded = checked(BytesUploaded + bytes);
        return true;
    }
}

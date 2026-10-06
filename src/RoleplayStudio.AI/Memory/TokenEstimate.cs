namespace RoleplayStudio.AI.Memory;

public static class TokenEstimate
{
    // About four characters a token in English prose: close enough to budget with, and no tokenizer per model.
    public static int Of(string text) => (text.Length + 3) / 4;
}

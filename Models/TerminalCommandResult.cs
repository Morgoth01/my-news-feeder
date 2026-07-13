namespace MyNewsFeeder.Models
{
    public class TerminalCommandResult
    {
        public string Output { get; set; } = string.Empty;
        public bool ClearScreen { get; set; }
        public bool CloseRequested { get; set; }
        public bool BrowseRequested { get; set; }
        public bool IsError { get; set; }
        public bool IsSuccess { get; set; }
        public string LineType { get; set; } = "Normal";

        public static TerminalCommandResult Text(string output, string lineType = "Normal", bool isError = false, bool isSuccess = false)
        {
            return new TerminalCommandResult 
            {
                Output = output ?? string.Empty,
                LineType = lineType,
                IsError = isError,
                IsSuccess = isSuccess
            };
        }

        public static TerminalCommandResult Error(string output)
        {
            return new TerminalCommandResult 
            {
                Output = output ?? string.Empty,
                LineType = "Error",
                IsError = true
            };
        }

        public static TerminalCommandResult Success(string output)
        {
            return new TerminalCommandResult 
            {
                Output = output ?? string.Empty,
                LineType = "Success",
                IsSuccess = true
            };
        }
    }
}
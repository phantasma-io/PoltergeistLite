namespace Poltergeist.Wallet
{
    /// <summary>
    /// Shared UI-agnostic primitives used by wallet application layers.
    /// </summary>
    public enum MessageKind
    {
        Default,
        Error,
        Success
    }

    public enum GUIState
    {
        Loading,
        Wallets,
        Balances,
        Nft,
        NftView,
        NftTransferList,
        History,
        Account,
        Sending,
        Confirming,
        WalletsManagement,
        Settings,
        ScanQR,
        Backup,
        Exit,
        Fatal
    }

    public enum PromptResult
    {
        Waiting,
        Failure,
        Success,
        Custom_1,
        Custom_2,
        Custom_3
    }

    public enum ModalState
    {
        None,
        Message,
        Input,
        Password,
    }

    public enum TtrsNftSortMode
    {
        None,
        Number_Date,
        Date_Number,
        Type_Number_Date,
        Type_Date_Number,
        Type_Rarity
    }

    public enum NftSortMode
    {
        None,
        Name,
        Number_Date,
        Date_Number
    }

    public enum SortDirection
    {
        None,
        Ascending,
        Descending
    }

    public enum ttrsNftType
    {
        All,
        Vehicle,
        Part,
        License
    }

    public enum ttrsNftRarity
    {
        All = 0,
        Consumer = 1,
        Industrial = 2,
        Professional = 3,
        Collector = 4
    }

    public enum nftMinted
    {
        All,
        Last_15_Mins,
        Last_Hour,
        Last_24_Hours,
        Last_Week,
        Last_Month
    }
}

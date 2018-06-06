namespace DataDump
{
    internal enum DatabaseType
    {
        MSSQL,
#if Net
        Oracle
#endif
    }
}

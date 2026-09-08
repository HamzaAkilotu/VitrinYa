using VitrinYa.Models;

namespace VitrinYa.Services;

public sealed record PagedResult<T>(List<T> Items, PageInfo Pager);

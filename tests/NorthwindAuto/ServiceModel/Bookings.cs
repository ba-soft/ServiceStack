﻿// Complete declarative AutoQuery services for Bookings CRUD example:
// https://docs.servicestack.net/autoquery-crud-bookings

using System;
using ServiceStack;
using ServiceStack.DataAnnotations;

namespace MyApp.ServiceModel;

public class Booking : AuditBase
{
    [AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; }
    public RoomType RoomType { get; set; }
    public int RoomNumber { get; set; }
    public DateTime BookingStartDate { get; set; }
    public DateTime? BookingEndDate { get; set; }
    public decimal Cost { get; set; }
    public string Notes { get; set; }
    public bool? Cancelled { get; set; }
}

public enum RoomType
{
    Single,
    Double,
    Queen,
    Twin,
    Suite,
}

[Api("Find Bookings")]
[Tag("bookings")]
[AutoApply(Behavior.AuditQuery)]
public class QueryBookings : QueryDb<Booking> 
{
    public int? Id { get; set; }
}

// Uncomment below to enable DeletedBookings API to view deleted bookings:
// [Route("/bookings/deleted")]
// [AutoFilter(QueryTerm.Ensure, nameof(AuditBase.DeletedDate), Template = SqlTemplate.IsNotNull)]
// public class DeletedBookings : QueryDb<Booking> {}

[Api("Create a new Booking")]
[Tag("bookings")]
[ValidateHasRole("Employee")]
[ValidateHasPermission("ThePermission")]
[AutoApply(Behavior.AuditCreate)]
public class CreateBooking : ICreateDb<Booking>, IReturn<IdResponse>
{
    [Description("Name this Booking is for")]
    public string Name { get; set; }
    [ApiAllowableValues(typeof(RoomType))]
    public RoomType RoomType { get; set; }
    [ValidateGreaterThan(0)]
    public int RoomNumber { get; set; }
    public DateTime BookingStartDate { get; set; }
    public DateTime? BookingEndDate { get; set; }
    [ValidateGreaterThan(0)]
    public decimal Cost { get; set; }
    public string Notes { get; set; }
}

[Api("Update an existing Booking")]
[Tag("bookings")]
[ValidateHasRole("Employee")]
[AutoApply(Behavior.AuditModify)]
public class UpdateBooking : IPatchDb<Booking>, IReturn<IdResponse>
{
    public int Id { get; set; }
    public string Name { get; set; }
    [ApiAllowableValues(typeof(RoomType))]
    public RoomType? RoomType { get; set; }
    [ValidateGreaterThan(0)]
    public int? RoomNumber { get; set; }
    public DateTime? BookingStartDate { get; set; }
    public DateTime? BookingEndDate { get; set; }
    [ValidateGreaterThan(0)]
    public decimal? Cost { get; set; }
    public string Notes { get; set; }
    public bool? Cancelled { get; set; }
}

[Api("Delete a Booking")]
[Tag("bookings")]
[ValidateHasRole("Manager")]
[AutoApply(Behavior.AuditSoftDelete)]
public class DeleteBooking : IDeleteDb<Booking>, IReturnVoid
{
    public int Id { get; set; }
}
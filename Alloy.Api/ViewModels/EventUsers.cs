using System.Security.Claims;
using System;
using System.Collections.Generic;
using Player.Api.Models;

namespace Alloy.Api.ViewModels
{
  public class EventUser : Base
  {
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
  }
}
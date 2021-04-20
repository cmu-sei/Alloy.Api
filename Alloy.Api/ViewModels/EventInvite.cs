using System;
using System.Collections.Generic;
using Player.Api.Models;

namespace Alloy.Api.ViewModels
{
  public class EventInvite : Base
  {
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string ShareCode { get; set; }
  }
}
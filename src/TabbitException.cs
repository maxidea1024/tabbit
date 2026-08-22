using Tabbit.Models;
using System;
using System.Collections.Generic;

namespace Tabbit;

/// <summary>
/// Tabbit Exception.
/// </summary>
public class TabbitException : Exception
{
    /// <summary>
    /// Detail error
    /// </summary>
    public class Detail
    {
        /// <summary>
        /// The sheet cell location where the error occurred.
        /// </summary>
        public Location? Location { get; set; }

        /// <summary>
        /// Error message.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Which report this is, for the ones that have been named. Null for the call sites
        /// still passing their text directly.
        /// </summary>
        /// <remarks>
        /// Carried beside the text rather than instead of it, so that a caller printing
        /// reports needs no catalog and a test can assert on the id without the wording.
        /// spec/message-ids.md §7.
        /// </remarks>
        public string? MessageId { get; set; }
    }

    /// <summary>
    /// The sheet cell location where the error occurred.
    /// </summary>
    public Location? Location { get; set; }

    /// <summary>
    /// Detail errors
    /// </summary>
    public List<Detail> Details { get; set; } = [];

    /// <summary>
    /// Default empty constructor.
    /// </summary>
    public TabbitException()
    {
    }

    /// <summary>
    /// Construct with message.
    /// </summary>
    /// <param name="message"></param>
    public TabbitException(string message) : base(message)
    {
    }

    /// <summary>
    /// Construct with message and inner exception.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inner"></param>
    public TabbitException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// Which report this is, for the ones that have been named.
    /// </summary>
    /// <remarks>
    /// Null while a call site still writes its own text. That is what makes the move
    /// measurable: a test naming an id cannot pass against a site that has not moved.
    /// </remarks>
    public string? MessageId { get; }

    /// <summary>
    /// Construct from a named report, whose text comes from the catalog in use.
    /// </summary>
    /// <remarks>
    /// The text is settled here rather than when the exception is printed, because an
    /// exception is expected to carry its own message - <see cref="Exception.Message"/> is
    /// read by handlers that know nothing about catalogs, and by the tests.
    /// </remarks>
    public TabbitException(Location? location, Messages.Message message)
        : base(message.In(Messages.MessageCatalog.Current))
    {
        Location = location;
        MessageId = message.Id;
    }

    /// <summary>
    /// Construct from a named report and the exception that caused it.
    /// </summary>
    /// <remarks>
    /// For the reports that quote something they caught. The quoted text arrives as a value
    /// like any other; the exception is kept as well, so a defect underneath a well-worded
    /// report still has its stack.
    /// </remarks>
    public TabbitException(Messages.Message message, Exception inner)
        : base(message.In(Messages.MessageCatalog.Current), inner)
    {
        MessageId = message.Id;
    }

    /// <summary>
    /// Construct with cell-location and message.
    /// </summary>
    /// <param name="location"></param>
    /// <param name="message"></param>
    public TabbitException(Location? location, string message) : base(message)
    {
        Location = location;
    }

    /// <summary>
    /// Construct with cell-location, message and inner exception.
    /// </summary>
    /// <param name="location"></param>
    /// <param name="message"></param>
    /// <param name="inner"></param>
    public TabbitException(Location? location, string message, Exception inner) : base(message, inner)
    {
        Location = location;
    }
}

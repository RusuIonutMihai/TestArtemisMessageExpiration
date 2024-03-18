using Amqp;
using Amqp.Framing;

const string userName = "test";
const string password = "test";
const string address = $"amqp://{userName}:{password}@localhost:5672";
const string targetAddress = "MyQueue";
const string targetQueue = "MyQueue";
const string expiryAddress = "ExpiryQueue";
const string expiryQueue = "ExpiryQueue";

IConnection connection = null;
ISession session = null;
IReceiverLink receiver = null;
ISenderLink sender = null;

object lockObj = new object();

var sentMessageCount = 0;
var expiredMessageCount = 0;
var testDurationInSeconds = 0;
var numberOfMessagesPerSecond = 0;
var lastExpiredMessageTime = DateTime.MaxValue;

try
{
    ConnectionFactory factory = new();
    connection = await factory.CreateAsync(new Address(address));
    session = connection.CreateSession();

    receiver = session.CreateReceiver("Exp_receiver", new Source() { Address = $"{expiryAddress}::{expiryQueue}", Durable = 1 }); // Receiver for expired messages moved to expiry queue
    receiver.Start(50, (link, message) =>
    {
        receiver.Accept(message);
        lock (lockObj)
        {
            expiredMessageCount++;
            lastExpiredMessageTime = DateTime.Now;
        }
    });

    sender = session.CreateSender("Msg_sender", new Target() { Address = $"{targetAddress}", Durable = 1 });

    Console.Write("Enter the test duration in seconds: ");
    if (!Int32.TryParse(Console.ReadLine(), out testDurationInSeconds) || testDurationInSeconds <= 0)
        throw new Exception("Invalid test duration");

    Console.Write("Enter the number of messages to send per second: ");
    if (!Int32.TryParse(Console.ReadLine(), out numberOfMessagesPerSecond) || numberOfMessagesPerSecond <= 0)
        throw new Exception("Invalid number of messages");

    Console.WriteLine("Starting test...");

    var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testDurationInSeconds));

    try
    {
        Console.WriteLine("Sending messages...");
        while (await periodicTimer.WaitForNextTickAsync(cts.Token))
        {
            var tasks = new List<Task>();

            for (var i = 0; i < numberOfMessagesPerSecond; i++)
            {
                var message = new Message("Test");
                message.Header = new Header() { Durable = true, Ttl = 60000 }; // expiry is 60 seconds
                message.Properties = new Properties() { MessageId = Guid.NewGuid().ToString(), Subject = "MyFilter" };
                tasks.Add(sender.SendAsync(message));
                sentMessageCount++;
            }

            await Task.WhenAll(tasks);
        }
    }
    catch (TaskCanceledException) { }

    Console.WriteLine($"Finished sending messages. Messages sent: {sentMessageCount}. Waiting some time for all messages to expire.");

    var cts2 = new CancellationTokenSource();

    // This timer will check if there are any expired messages in the last 10 seconds. If not, it will stop the test
    var expiryTimer = new System.Timers.Timer(1000);
    expiryTimer.AutoReset = true;
    expiryTimer.Elapsed += (sender, e) =>
    {
        lock (lockObj)
        {
            if (DateTime.Now - lastExpiredMessageTime > TimeSpan.FromSeconds(10))
            {
                expiryTimer.Stop();
                cts2.Cancel();
            }
        }
    };

    expiryTimer.Start();

    try 
    {
        await Task.Delay(TimeSpan.FromMinutes(5), cts2.Token);
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine($"Test completed. Expired messages received: {expiredMessageCount}");

        if (sentMessageCount != expiredMessageCount)
        {
            Console.WriteLine($"{sentMessageCount - expiredMessageCount} messages are not received.");
        }

        return;
    }

    Console.WriteLine($"Something went wrong, the test should be completed by now. Expired messages received: {expiredMessageCount}");
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}
finally
{
    if (sender != null && !sender.IsClosed)
    {
        await sender.DetachAsync(null);
    }

    if (receiver != null && !receiver.IsClosed)
    {
        await receiver.DetachAsync(null);
    }

    if (session != null && !session.IsClosed)
    {
        await session.CloseAsync();
    }

    if (connection != null && !connection.IsClosed)
    {
        await connection.CloseAsync();
    }
}
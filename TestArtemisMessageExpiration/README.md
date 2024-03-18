# Test Artemis Message Expiration

This project was created to test the ActiveMQ Artemis message expiration feature.
The project contains a simple producer that sends the messages to an address (MyQueue) and consumer that receives the expired messages from the expiry queue.
The messages expire after one minute.

The project connects to the message broker using default address: "amqp://localhost:5672", and for authentication, it uses the user/pass: test/test.

When the application is started, it asks for the number of seconds that the test should run, and the number of messages that should to send per second.
After all messages are sent, the application waits for the remaining messages to expire (maximum 2 minutes).

After the test is completed, the application prints the number of expired messages that are received and the number of missing messages (if any).

Use the provided [broker.xml](/Config/broker.xml) file to configure your broker.
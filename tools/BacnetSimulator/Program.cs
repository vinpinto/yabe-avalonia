using System.IO.BACnet;
using System.IO.BACnet.Storage;

var xmlPath = Path.Combine(AppContext.BaseDirectory, "DeviceDescriptor.xml");
if (!File.Exists(xmlPath))
    xmlPath = Path.Combine(Directory.GetCurrentDirectory(), "DeviceDescriptor.xml");

var storage = DeviceStorage.Load(xmlPath, 1234);
Console.WriteLine($"Loaded device storage: {storage.Objects.Length} objects, Device ID={storage.DeviceId}");

var transport = new BacnetIpUdpProtocolTransport(0xBAC0);
var server = new BacnetClient(transport);

server.OnWhoIs += (sender, adr, lowLimit, highLimit) =>
{
    var myId = storage.DeviceId;
    if (lowLimit != -1 && myId < lowLimit) return;
    if (highLimit != -1 && myId > highLimit) return;
    sender.Iam(myId, new BacnetSegmentations());
    Console.WriteLine($"  WhoIs from {adr} -> Iam {myId}");
};

server.OnReadPropertyRequest += (sender, adr, invokeId, objectId, property, maxSegments) =>
{
    lock (storage)
    {
        var status = storage.ReadProperty(objectId, (BacnetPropertyIds)property.propertyIdentifier,
            property.propertyArrayIndex, out var value);
        if (status == DeviceStorage.ErrorCodes.Good)
        {
            sender.ReadPropertyResponse(adr, invokeId, sender.GetSegmentBuffer(maxSegments),
                objectId, property, value);
        }
        else
        {
            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY,
                invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT,
                BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY);
        }
    }
};

server.OnReadPropertyMultipleRequest += (sender, adr, invokeId, properties, maxSegments) =>
{
    lock (storage)
    {
        try
        {
            var values = new List<BacnetReadAccessResult>();
            foreach (var rap in properties)
            {
                storage.ReadPropertyAll(rap.objectIdentifier, out var propValues);
                values.Add(new BacnetReadAccessResult(rap.objectIdentifier, propValues));
            }
            sender.ReadPropertyMultipleResponse(adr, invokeId,
                sender.GetSegmentBuffer(maxSegments), values);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  RPM error: {ex.Message}");
        }
    }
};

server.OnWritePropertyRequest += (sender, adr, invokeId, objectId, value, maxSegments) =>
{
    lock (storage)
    {
        var status = storage.WriteProperty(objectId,
            (BacnetPropertyIds)value.property.propertyIdentifier,
            value.property.propertyArrayIndex, value.value);
        if (status == DeviceStorage.ErrorCodes.Good)
        {
            sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY,
                invokeId);
            Console.WriteLine($"  Write to {objectId}");
        }
        else
        {
            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY,
                invokeId, BacnetErrorClasses.ERROR_CLASS_OBJECT,
                BacnetErrorCodes.ERROR_CODE_WRITE_ACCESS_DENIED);
        }
    }
};

server.Start();
Console.WriteLine("BACnet simulator running on port 47808 (0xBAC0)");
Console.WriteLine("Objects: AI:0 (Temp), AI:1 (Humidity), AO:0 (Setpoint), BI:0 (Occupancy), BO:0 (Fan)");
Console.WriteLine("Press Ctrl+C to stop...");

var rng = new Random();
var timer = new System.Timers.Timer(5000);
timer.Elapsed += (s, e) =>
{
    lock (storage)
    {
        var temp = 20.0f + (float)(rng.NextDouble() * 5.0);
        var humidity = 45.0f + (float)(rng.NextDouble() * 20.0);
        var tempValues = new BacnetValue[] { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, temp) };
        var humValues = new BacnetValue[] { new(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, humidity) };
        storage.WriteProperty(
            new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0),
            BacnetPropertyIds.PROP_PRESENT_VALUE, System.IO.BACnet.Serialize.ASN1.BACNET_ARRAY_ALL, tempValues);
        storage.WriteProperty(
            new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 1),
            BacnetPropertyIds.PROP_PRESENT_VALUE, System.IO.BACnet.Serialize.ASN1.BACNET_ARRAY_ALL, humValues);
        Console.WriteLine($"  Sim: Temp={temp:F1}C, Humidity={humidity:F1}%");
    }
};
timer.Start();

Thread.Sleep(Timeout.Infinite);

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Aethon.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Tests
{
    [TestFixture, ExcludeFromCodeCoverage]
    internal class SmallBlockMemoryStreamTests
    {
        private static readonly int[] NoAllocations = new int[0];

        private static readonly byte[] BaseDataPattern = 
        {
            0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef
        };

        private static byte[] MakeTestData(long length)
        {
            var data = new byte[length];

            byte iter = 0;
            for (var i = 0; i < length; )
            {
                for (var mod = 0; mod < BaseDataPattern.Length && i < length; mod++, i++)
                    data[i] = (byte)(BaseDataPattern[mod] + iter);
                iter++; // allow this to roll
            }

            return data;
        }

        // Generate the expected allocations for a given set of writes
        // (these functions know a lot about how the subject is managing its internals,
        //  but since the internal performance is critical to the value of the subject
        //  we accept the risk)
        private static int[] AllocationsForWrites(params int[] writes)
        {
            var capacity = 0L;
            return AllocationsForRequirements(writes.Select(x => capacity = capacity + x).ToArray());
        }

        // Generate the expected allocations for a given set of capacity requirements
        private static int[] AllocationsForRequirements(params long[] required)
        {
            if (required == null || required.Length == 0)
                return NoAllocations;

            var result = new List<int>();
            var capacity = 0;
            var nextSizeBreak = SmallBlockMemoryStream.StartBlockCount;
            foreach (var req in required)
            {
                while (capacity < req)
                {
                    var desired = Math.Max(SmallBlockMemoryStream.MinBlockSize, Math.Max(capacity * 2, req) - capacity);
                    var alloc = (int)Math.Min(SmallBlockMemoryStream.MaxBlockSize, desired);
                    capacity += alloc;
                    result.Add(alloc);
                    if (result.Count > nextSizeBreak)
                        nextSizeBreak *= 2;
                }
            }
            if (result.Count > 0)
                while (result.Count < nextSizeBreak)
                    result.Add(-1);
            return result.ToArray();
        }

        //private static readonly byte[] NoData = new byte[0];
        //private static readonly byte[] SmallBlock = MakeTestData(SmallBlockMemoryStream.MinBlockSize - 1);
        //private static readonly byte[] OneBlock = MakeTestData(SmallBlockMemoryStream.MinBlockSize);
        //private static readonly byte[] BlockPlus = MakeTestData(SmallBlockMemoryStream.MinBlockSize + 1);
        //private static readonly byte[] LargeBlock = MakeTestData(5000);
        //private static readonly byte[] HugeData = MakeTestData(86000);


        private static readonly byte[] TestData = MakeTestData(86000);
        // private static readonly byte[] NoData = new byte[0];
        private const int SmallBlockSize = SmallBlockMemoryStream.MinBlockSize - 1;
        private const int OneBlock = SmallBlockMemoryStream.MinBlockSize;
        private const int BlockPlus = SmallBlockMemoryStream.MinBlockSize + 1;
        private const int LargeBlock = 5000;
        private const int HugeData = 86000;

        private static FluentAssertions.Equivalency.EquivalencyAssertionOptions<SmallBlockMemoryStream> EqOpts(
            FluentAssertions.Equivalency.EquivalencyAssertionOptions<SmallBlockMemoryStream> options)
        {
            return options.ExcludingMissingProperties();
        }

        [Test]
        public void NewStream_isInTheCorrectState()
        {
            var subject = new SmallBlockMemoryStream();

            subject.ShouldBeEquivalentTo(new
            {
                CanRead = true,
                CanSeek = true,
                CanWrite = true,
                Length = 0,
                Position = 0
            }, EqOpts);
            subject.GetAllocationSizes().ShouldBeEquivalentTo(NoAllocations);
        }

        //[Test]
        //public void Read_OnANewStream_Returns0()
        //{
        //    TestWrite(s => { },
        //        (Stream s, out byte[] b) =>
        //        {
        //            b = new byte[1];
        //            return s.Read(b, 0, 1);
        //        });
        //}

        //private static void TestWrites(params byte[][] data)
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    var length = 0;
        //    int[] expectedAllocations = NoAllocations;
        //    for (var i = 0; i < data.Length; i++)
        //    {
        //        var d = data[i];
        //        subject.Write(d, 0, d.Length);
        //        length += d.Length;

        //        expectedAllocations = AllocationsForWrites(data.Take(i + 1).Select(x => x.Length).ToArray());
        //        subject.ShouldBeEquivalentTo(new
        //        {
        //            Length = length,
        //            Position = length,
        //            Allocations = expectedAllocations
        //        }, EqOpts);
        //    }

        //    subject.Position = 0;
        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = length,
        //        Position = 0,
        //        Allocations = expectedAllocations
        //    }, EqOpts);

        //    var buffer = new byte[length];
        //    subject.Read(buffer, 0, length).Should().Be(length);
        //}

        [Test]
        public void WriteNothing_OnANewStream_DoesNothing()
        {
            VerifyAction(s => s.Write(TestData, 0, 0));
        }

        [Test]
        public void Write_RequiringOneAllocationToAnEmptyStream_succeeds()
        {
            VerifyAction(s => s.Write(TestData, 0, SmallBlockSize));
        }

        [Test]
        public void Write_RequiringExactlyOneBlockToAnEmptyStream_succeeds()
        {
            VerifyAction(s => s.Write(TestData, 0, OneBlock));
        }

        [Test]
        public void Write_RequiringMultipleAllocationsToAnEmptyStream_succeeds()
        {
            VerifyAction(s => s.Write(TestData, 0, BlockPlus));
        }

        [Test]
        public void Write_FromMiddleOfABlock_succeeds()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, SmallBlockSize);
                s.Write(TestData, 0, SmallBlockSize);
            });
        }

        [Test]
        public void Write_FromEndOfABlock_succeeds()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, OneBlock);
                s.Write(TestData, 0, SmallBlockSize);
            });
        }

        [Test]
        public void SetLength_OnANewStream_succeeds()
        {
            VerifyAction(s =>
                s.SetLength(5123)
            );
        }

        [Test]
        public void SetLength_whenGrowing_succeeds()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, 512);
                s.SetLength(10000);
            });
        }

        [Test]
        public void SetLength_RequiringLotsOfAllocations_succeeds()
        {
            VerifyAction(s => s.SetLength(5000000));
        }

        [Test]
        public void Read_OnAStreamPartiallySizedBySetLength_succeeds()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, BlockPlus);
                s.SetLength(0);
                s.SetLength(BlockPlus);
            });
        }

        [Test]
        public void Read_OnAStreamSizedBySetLengthToMidBlock_ReturnsClearedData()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, OneBlock);
                s.Write(TestData, 0, OneBlock);
                s.SetLength(BlockPlus);
                s.SetLength(OneBlock * 2);
            });
        }

        [Test]
        public void WriteByte_succeeds()
        {
            VerifyAction(s =>
            {
                for (var i = 0; i < BlockPlus; i++)
                    s.WriteByte(TestData[i]);
            });
        }


        [Test]
        public void ReadByte_PastEndOfStream_succeeds()
        {
            using (var standard = new MemoryStream())
            using (var subject = new SmallBlockMemoryStream())
            {
                standard.Write(TestData, 0, TestData.Length);
                subject.Write(TestData, 0, TestData.Length);

                for (var i = 0; i <= TestData.Length; i++)
                    subject.ReadByte().Should().Be(standard.ReadByte());
            }
        }

        [Test]
        public void ReadByte_succeeds()
        {
            using (var standard = new MemoryStream())
            using (var subject = new SmallBlockMemoryStream())
            {
                standard.Write(TestData, 0, TestData.Length);
                standard.Position = 0;
                subject.Write(TestData, 0, TestData.Length);
                subject.Position = 0;

                for (var i = 0; i <= TestData.Length; i++)
                    subject.ReadByte().Should().Be(standard.ReadByte());
            }
        }

        // TODO
        //[Test]
        //public void WriteASmallBlock_OnANewStream_CreatesASingleAllocation()
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    subject.Write(SmallBlock, 0, SmallBlock.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = SmallBlock.Length,
        //        Position = SmallBlock.Length,
        //        Allocations = AllocationsForWrites(SmallBlock.Length)
        //    }, EqOpts);
        //}

        //[Test]
        //public void WriteAMediumBlock_OnANewStream_CreatesASingleAllocation()
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    subject.Write(BlockPlus, 0, BlockPlus.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = BlockPlus.Length,
        //        Position = BlockPlus.Length,
        //        Allocations = AllocationsForWrites(BlockPlus.Length)
        //    }, EqOpts);
        //}

        //[Test]
        //public void WriteAHugeBlock_OnANewStream_CreatesNeededAllocations()
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    subject.Write(HugeData, 0, HugeData.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = HugeData.Length,
        //        Position = HugeData.Length,
        //        Allocations = AllocationsForWrites(HugeData.Length)
        //    }, EqOpts);
        //}

        //[Test]
        //public void Read_PastEnd_ReadsTheRestAndReturnsCorrectLength()
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    subject.Write(BlockPlus, 0, BlockPlus.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = BlockPlus.Length,
        //        Position = BlockPlus.Length,
        //        Allocations = AllocationsForWrites(BlockPlus.Length)
        //    }, EqOpts);

        //    subject.Position = 0;
        //    var result = new byte[BlockPlus.Length + 1];
        //    subject.Read(result, 0, result.Length).Should().Be(BlockPlus.Length);
        //}

        //[Test]
        //public void WriteASmallBlock_WhilePositionedOnABoundary_CreatesNeededAllocation()
        //{
        //    var subject = new SmallBlockMemoryStream();
        //    subject.Write(OneBlock, 0, OneBlock.Length);

        //    subject.Write(SmallBlock, 0, SmallBlock.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = OneBlock.Length + SmallBlock.Length,
        //        Position = OneBlock.Length + SmallBlock.Length,
        //        Allocations = AllocationsForWrites(OneBlock.Length, SmallBlock.Length)
        //    }, EqOpts);
        //}

        //[Test]
        //public void WriteAHugeBlock_WhilePositionedOnABoundary_CreatesNeededAllocations()
        //{
        //    var subject = new SmallBlockMemoryStream();
        //    subject.Write(OneBlock, 0, OneBlock.Length);

        //    subject.Write(HugeData, 0, HugeData.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = OneBlock.Length + HugeData.Length,
        //        Position = OneBlock.Length + HugeData.Length,
        //        Allocations = AllocationsForWrites(OneBlock.Length, HugeData.Length)
        //    }, EqOpts);
        //}

        //[Test]
        //public void Read_Iteratively_Succeeds()
        //{
        //    var subject = new SmallBlockMemoryStream();

        //    subject.Write(HugeData, 0, HugeData.Length);

        //    subject.ShouldBeEquivalentTo(new
        //    {
        //        Length = HugeData.Length,
        //        Position = HugeData.Length,
        //        Allocations = AllocationsForWrites(HugeData.Length)
        //    }, EqOpts);

        //    subject.Position = 0;

        //    var result = new byte[HugeData.Length];
        //    var offset = 0;
        //    const int readLength = 4096;
        //    int read;
        //    do
        //    {
        //        read = subject.Read(result, offset, readLength);
        //        offset += read;
        //    } while (read == readLength);

        //    Assert.AreEqual(result, HugeData);
        //}

        [Test]
        public void Seek_BeyondEnd_Allocates()
        {
            var subject = new SmallBlockMemoryStream();

            subject.Seek(1000, SeekOrigin.Begin);

            subject.ShouldBeEquivalentTo(new
            {
                Length = 1000,
                Position = 1000,
                Allocations = AllocationsForWrites(1000)
            }, EqOpts);
        }

        [Test]
        public void SetLength_withBadParameters_fails()
        {
            var subject = new SmallBlockMemoryStream();
            Action action = () => subject.SetLength(-10);
            action.ShouldThrow<ArgumentException>();
        }


        [Test]
        public void Position_withBadParameters_fails()
        {
            var subject = new SmallBlockMemoryStream();
            Action action = () => subject.Position = -10;
            action.ShouldThrow<ArgumentOutOfRangeException>();
        }

        [Test]
        public void Seek_withBadParameters_fails()
        {
            var subject = new SmallBlockMemoryStream();
            Action action = () => subject.Seek(0, (SeekOrigin)123);
            action.ShouldThrow<ArgumentException>();
        }

        [Test]
        public void Seek_FromBeginBeforeBegin_throws()
        {
            VerifyThrows<IOException>(s => s.Seek(-1, SeekOrigin.Begin));
        }

        [Test]
        public void Seek_FromCurrentBeforeBegin_throws()
        {
            VerifyThrows<IOException>(s => s.Seek(-1, SeekOrigin.Current));
        }

        [Test]
        public void Seek_FromEndBeforeBegin_throws()
        {
            VerifyThrows<IOException>(s => s.Seek(-1, SeekOrigin.End));
        }

        [Test]
        public void Read_withBadParameters_fails()
        {
            var subject = new SmallBlockMemoryStream();
            var data = new byte[10];

            Action action = () => subject.Read(null, 0, 0);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Read(data, -10, 0);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Read(data, 0, -10);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Read(data, 0, 20);
            action.ShouldThrow<ArgumentException>();
        }

        [Test]
        public void Write_withBadParameters_fails()
        {
            var subject = new SmallBlockMemoryStream();
            var data = MakeTestData(10);

            Action action = () => subject.Write(null, 0, 0);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Write(data, -10, 0);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Write(data, 0, -10);
            action.ShouldThrow<ArgumentException>();

            action = () => subject.Write(data, 0, 20);
            action.ShouldThrow<ArgumentException>();
        }

        [Test]
        public void Flush_doesNotFail()
        {
            new SmallBlockMemoryStream().Flush();
        }

        [Test]
        public void WriteRead_forVariousLengths_succeeds()
        {
            var lengths = new[]
            {
                0, 16, 240, 256, 257, 400, 95000, 285000
            };

            foreach (var length in lengths)
            {
                WriteRead_succeeds(length, false);
                WriteRead_succeeds(length, true);
            };
        }

        public void WriteRead_succeeds(int length, bool prewrite)
        {
            var prelength = prewrite ? length : 0;
            var nonce = MakeTestData(prelength);

            var writeData = MakeTestData(length);

            var subject = new SmallBlockMemoryStream();

            if (prewrite)
                subject.Write(nonce, 0, length);

            var expectedAllocations = AllocationsForWrites(prelength, length);

            subject.Write(writeData, 0, writeData.Length);
            subject.ShouldBeEquivalentTo(new
            {
                Length = prelength + length,
                Position = prelength + length,
                Allocations = expectedAllocations
            }, EqOpts);

            subject.Position = prelength;
            subject.ShouldBeEquivalentTo(new
            {
                Length = prelength + length,
                Position = prelength,
                Allocations = expectedAllocations
            }, EqOpts);

            var readData = new byte[length];
            subject.Read(readData, 0, length);
            subject.ShouldBeEquivalentTo(new
            {
                Length = prelength + length,
                Position = prelength + length,
                Allocations = expectedAllocations
            }, EqOpts);

            // fluent assertions are very slow using readData.Should().BeEquivalentTo(writeData);
            //  => NUnit assertion here
            Assert.AreEqual(readData, writeData);
        }

        [Test]
        public void ReadPastEnd_succeedsWithCorrectReadLength()
        {
            const int dataLength = 100;
            const int readLength = 110;
            var writeData = MakeTestData(dataLength);
            var subject = new SmallBlockMemoryStream();
            subject.Write(writeData, 0, dataLength);
            subject.Position = 0;
            var readData = new byte[readLength];
            var read = subject.Read(readData, 0, readLength);
            Assert.AreEqual(writeData.Length, read);
        }

        [Test]
        public void SetLength_whenShrinking_succeeds()
        {
            const int initialLength = 10000;
            const int truncatedLength = 512;
            var writeData = MakeTestData(initialLength);
            var subject = new SmallBlockMemoryStream();
            subject.Write(writeData, 0, initialLength);
            subject.Length.Should().Be(initialLength);
            subject.Position.Should().Be(initialLength);

            subject.SetLength(truncatedLength);
            subject.Length.Should().Be(truncatedLength);
            subject.Position.Should().Be(truncatedLength);

            subject.Position = 0;
            var readData = new byte[initialLength];
            subject.Read(readData, 0, initialLength);

            Array.Clear(writeData, truncatedLength, initialLength - truncatedLength);
            Assert.AreEqual(readData, writeData);
        }

        [Test]
        public void Seeking_succeeds()
        {
            const int length = 100;
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Seek(0, SeekOrigin.Begin);
            });
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Seek(10, SeekOrigin.Begin);
            });
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Seek(0, SeekOrigin.End);
            });
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Seek(10, SeekOrigin.End);
            });
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Position = 50;

                s.Seek(10, SeekOrigin.Current);
            });
            VerifyAction(s =>
            {
                s.Write(TestData, 0, length);
                s.Position = 50;

                s.Seek(-30, SeekOrigin.Current);
            });
        }

        [Test]
        public void SetPosition_BeyondEndOfStream_succeeds()
        {
            VerifyAction(s =>
            {
                s.Write(TestData, 0, 10);
                s.Position = 50;
            });
        }

        private static void VerifyThrows<T>(Action<Stream> action) where T : Exception
        {
            using (var standard = new MemoryStream())
            using (var subject = new SmallBlockMemoryStream())
            {
                ((Action)(() => action(standard))).ShouldThrow<T>();
                ((Action)(() => action(subject))).ShouldThrow<T>();
            }
        }

        private static void VerifyAction(Action<Stream> action)
        {
            using (var standard = new MemoryStream())
            using (var subject = new SmallBlockMemoryStream())
            {
                action(standard);
                action(subject);

                // length and position should be identical to standard
                subject.Length.Should().Be(standard.Length);
                subject.Position.Should().Be(standard.Position);

                // allocations should never exceed LOH limit
                var allocationSizes = subject.GetAllocationSizes();
                allocationSizes.Any(x => x > SmallBlockMemoryStream.MaxBlockSize)
                    .Should().BeFalse();

                // total allocation should be identical to the standard until the LOH limit
                //  is exceeded...
                if (subject.Length < SmallBlockMemoryStream.MaxBlockSize)
                {
                    allocationSizes.Sum(x => Math.Max(0, x))
                        .Should().Be(standard.Capacity);
                }
                else
                {
                    // ... then it should be within one LOH unit of the standard allocation
                    allocationSizes.Sum(x => Math.Max(0, x))
                        .Should()
                        .BeLessOrEqualTo((standard.Capacity % SmallBlockMemoryStream.MaxBlockSize + 1) *
                                         SmallBlockMemoryStream.MaxBlockSize);
                }

                // contents of the stream should be identical to the standard
                Assert.AreEqual(standard, subject);
            }
        }

        #region Disposal

        [Test]
        public void Dispose_LeavesSafePropertiesInTheCorrectState()
        {
            var subject = new SmallBlockMemoryStream();

            subject.Dispose();

            subject.ShouldBeEquivalentTo(new
            {
                CanRead = false,
                CanSeek = false,
                CanWrite = false,
            }, EqOpts);
            subject.GetAllocationSizes().ShouldBeEquivalentTo(NoAllocations);
        }

        [Test]
        public void Flust_AfterDispose_DoesNotThrow()
        {
            // Just to mimic the MemoryStream implementation
            var subject = new SmallBlockMemoryStream();
            subject.Dispose();

            subject.Flush();
        }

        [Test]
        public void AfterDispose_UnusableMethodsThrow()
        {
            var subject = new SmallBlockMemoryStream();
            subject.Dispose();

            long dummy;
            var buffer = new byte[1];
            var actions = new Action[]
            {
                () => dummy = subject.Length,
                () => subject.SetLength(0),
                () => dummy = subject.Position,
                () => subject.Position = 0,
                () => subject.Seek(0, SeekOrigin.Begin),
                () => subject.Write(buffer, 0, 1),
                () => subject.WriteByte(1),
                () => subject.Read(buffer, 0, 1),
                () => subject.ReadByte()
            };

            foreach (var action in actions)
                action.ShouldThrow<ObjectDisposedException>();
        }
        #endregion
    }
}

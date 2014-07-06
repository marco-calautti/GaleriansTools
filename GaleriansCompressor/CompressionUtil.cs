using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace GaleriansCompression
{
    sealed public class CompressionUtil
    {
        //when the compression ratio goes below this treshold we stop constructing the dictionary
        private const int FREE_BYTECODES_TRESHOLD = 56;
        private const int MINIMUM_PAIR_OCCURRENCES = 3;

        //prevent the construction of this utility class
        private CompressionUtil() { }

        public static void Compress(string inputFile, string outputFile)
        {
            byte[] inputBuffer = File.ReadAllBytes(inputFile);

            int lastPosition = 0;

            MemoryStream outputStream = new MemoryStream(inputBuffer.Length);

            while (lastPosition >= 0)
            {
                //this will contain the amount of byte codes not occurring in the file (such codes can be used as index for dictionary's entries)
                int freeByteCodes;
                int[] frequencies;
                //Here we construct a dictionary version of the raw input, in order to replace pairs with a single object (may be with raw bytes it becomes much faster)
                //This function tries to find a portion of the input that gives FREE_BYTECODES_TRESHOLD free byte codes to be used as indexes for the dictionary
                LinkedList<RecursivePair> inputData = ConstructInputDataAndByteFrequencies(inputBuffer, out frequencies, out freeByteCodes, ref lastPosition);

                //This will contain only the top-k recursive pair found in the input file
                LinkedList<RecursivePair> outputDictionary = new LinkedList<RecursivePair>();

                //we want to construct only a number of recursive pairs that can be indexed by the free byte codes
                for (int i = 0; i < freeByteCodes; i++)
                {
                    //construct from the input data, the most recurring (recursive) pair
                    RecursivePair mostFrequent = FindMostFrequentPair(inputData);

                    //if we found that the most recurring pair does not appear more than MINIMUM_PAIR_OCCURRENCES times, we stop here
                    if (mostFrequent == null) break;

                    //add the found pair to the dictionary in order (from the smallest to the largest one)
                    outputDictionary.AddLast(mostFrequent);

                    //Here we replace every pair corresponding with the most recurring one, with a single object representing it
                    ModifyInput(inputData, mostFrequent);

                }

                //Once the dictionary is ready, we store the dictionary and the compressed portion of input on which the dictionary has been constructed
                PackCompressedData(inputData, outputDictionary, frequencies, outputStream);
            }

            BinaryWriter stream = new BinaryWriter(File.Open(outputFile, FileMode.Create));
            stream.Write((UInt32)(outputStream.Length));
            stream.Write(outputStream.GetBuffer(), 0, (int)outputStream.Length);
            outputStream.Close();
            stream.Close();
        }

        //stores the dictionary and the relative compressed data to raw byte output
        private static void PackCompressedData(LinkedList<RecursivePair> inputData, LinkedList<RecursivePair> outputDictionary, int[] frequencies, MemoryStream outputStream)
        {

            Dictionary<RecursivePair, byte> encodedPairs;
            StoreDictionary(inputData, outputDictionary, frequencies, outputStream, out encodedPairs);

            //before storing the compressed data, we need to store its size in BIG ENDIAN order (I wonder why...)
            int compressedSize = inputData.Count;
            byte firstByte = (byte)((compressedSize >> 8) & 0xFF);
            byte secondByte = (byte)(compressedSize & 0xFF);
            outputStream.WriteByte(firstByte);
            outputStream.WriteByte(secondByte);

            foreach (RecursivePair pair in inputData)
            {
                if (pair.IsLeaf)
                    outputStream.WriteByte(pair.Value);
                else
                    outputStream.WriteByte(encodedPairs[pair]);
            }

        }

        //this function encodes the dictionary into raw bytes (I tried to make the byte representation of the dictionary as compact as I could)
        private static void StoreDictionary(LinkedList<RecursivePair> inputData, LinkedList<RecursivePair> outputDictionary, int[] frequencies, MemoryStream outputStream, out Dictionary<RecursivePair, byte> encodedPairs)
        {
            int dictionaryCursor = 0;

            int lastMTypeCount = 0;
            long lastMTypePosition = -1;

            encodedPairs = new Dictionary<RecursivePair, byte>();
            LinkedList<RecursivePair>.Enumerator currentPair = outputDictionary.GetEnumerator();

            for (int i = 0; i < frequencies.Length && encodedPairs.Count < outputDictionary.Count; i++)
            {
                //let's check whether we can find a free byte code to use to represent a pair in the binary dictionary
                if (frequencies[i] != 0)
                    continue;

                //the delta between the current cursor and the new byte code
                int delta = i - dictionaryCursor;

                //if we cannot do a complete shift with just one J-Type code because there are too many non-zero-frequency sequential byte codes (> 0x80).
                //We need to use two J-type codes: one dummy shift and then the remaining shift.
                if (delta > 0x80)
                {

                    //We know for sure that from the current cursor to the new one (i), there are byte codes with non-zero frequency only.
                    //Thus, these byte codes are forced to be mapped to theirselves. One of these byte codes is the byte code which has a delta
                    //of exactly 0x80 from the previous cursor. We use it to encode the dummy shift.

                    //dummy shift
                    outputStream.WriteByte((byte)(0x80 | 0x7F)); //maximum shift allowed = 0x7F + 1
                    dictionaryCursor += 0x80;
                    outputStream.WriteByte((byte)(dictionaryCursor & 0xFF)); //map the cursor to itself (it is safe to do this, see previous comments)

                    dictionaryCursor++;
                    i--; //we procastinate the shift of the current byte code at the next step
                    continue;
                }
                //if the next free byte code is not the next one from the current dictionary cursor, we are forced to use a J-Type store format
                else if (delta >= 1)
                {
                    //if we are the JType after a MType was completed, reset all of its stuff
                    if (lastMTypePosition >= 0)
                    {
                        lastMTypePosition = -1;
                        lastMTypeCount = 0;
                    }

                    //write the shift
                    outputStream.WriteByte((byte)(0x80 | (delta - 1) & 0xFF));

                    //move the cursor to the free byte code
                    dictionaryCursor += delta;
                }
                else //otherwise we can try to store this and the next (hopefully) sequential pairs in M-Type format
                {

                    //we are starting an MType encode anew
                    if (lastMTypePosition < 0)
                    {
                        //remember the counter position
                        lastMTypePosition = outputStream.Position;

                        //write the dummy counter first
                        outputStream.WriteByte((byte)00);

                    }
                    //we are continuing with the MType encoding

                    //increment the counter
                    outputStream.Position = lastMTypePosition;
                    outputStream.WriteByte((byte)((lastMTypeCount++) & 0xFF)); //the counter is -1 in the raw binary
                    outputStream.Seek(0, SeekOrigin.End);
                }

                //store the byte encoding for the current pair
                currentPair.MoveNext();
                encodedPairs[currentPair.Current] = (byte)(i & 0xFF);

                //encode the pair in the raw dictionary
                outputStream.WriteByte(currentPair.Current.First.IsLeaf ?
                                            currentPair.Current.First.Value :
                                            encodedPairs[currentPair.Current.First]);

                outputStream.WriteByte(currentPair.Current.Second.IsLeaf ?
                                            currentPair.Current.Second.Value :
                                            encodedPairs[currentPair.Current.Second]);

                dictionaryCursor++;
            }

            //here we need to shift the dictionary cursor to 0x100, to let the decompressor known when to stop.
            //Note that at this point the cursor may be 0x100 it self, so we need to check what kind 
            int lastDelta = 0x100 - dictionaryCursor;
            //same as above, we cannot store this shift with just one code
            if (lastDelta > 0x80)
            {
                outputStream.WriteByte((byte)(0x80 | 0x7F));
                dictionaryCursor += 0x80;
                outputStream.WriteByte((byte)(dictionaryCursor & 0xFF));
                dictionaryCursor++;
            }

            lastDelta = 0x100 - dictionaryCursor;
            if (lastDelta >= 1)
            {
                //write the shift
                outputStream.WriteByte((byte)(0x80 | (lastDelta - 1) & 0xFF));
                dictionaryCursor += lastDelta; //just to check whether the cursor ends at 0x100
            }
        }

        private static LinkedList<RecursivePair> ConstructInputDataAndByteFrequencies(byte[] buffer, out int[] frequencies, out int freeByteCodes, ref int start)
        {
            //occurrences of every byte code in the file
            frequencies = new int[256];

            //# byte codes not occurring at all in the input file
            freeByteCodes = 256;

            LinkedList<RecursivePair> list = new LinkedList<RecursivePair>();
            for (int i = start; i < buffer.Length; i++)
            {

                list.AddLast(new RecursivePair(buffer[i]));

                if (frequencies[buffer[i]]++ == 0)
                    freeByteCodes--;

                //this is how the original algorithm stops
                if (freeByteCodes == FREE_BYTECODES_TRESHOLD)
                {
                    start = (i == buffer.Length - 1) ? -1 : i + 1;
                    return list;
                }
            }
            start = -1;
            return list;

        }

        private static RecursivePair FindMostFrequentPair(LinkedList<RecursivePair> inputList)
        {
            //This dictionary will contain the number of occurrencies of every recursiver pair found in the input file
            Dictionary<RecursivePair, int> sequenciesFrequencies = new Dictionary<RecursivePair, int>();

            //this is the currently analyzed pair in the input data
            RecursivePair currentSearchingNode = new RecursivePair();


            //these two variables store the best pair (with its occurrences) found so far in the loop
            RecursivePair currentMax = new RecursivePair();
            int currentMaxOccur = 0;

            //we get the first element of the pair we want to analyze
            LinkedListNode<RecursivePair> node = inputList.First;


            while (node != null && node.Next != null)
            {

                currentSearchingNode.First = node.Value;
                //second part of the currently analyzed pair
                LinkedListNode<RecursivePair> nextNode = node.Next;
                currentSearchingNode.Second = nextNode.Value;

                int occur;

                //try to get the actual number of occurrences for this pair, if it never appeared until now, "occur" will be zero
                sequenciesFrequencies.TryGetValue(currentSearchingNode, out occur);

                //since we continuosly modify "currentSearchingNode" during the loop, if this pair needs to be added for the first time to the dictionary
                //we better provide a fresh new Node object. Otherwise, we can safely use "currentSearchingNode" as key, since it will be used
                //just to find the value in the dictionary.
                sequenciesFrequencies[occur == 0 ? new RecursivePair(currentSearchingNode) : currentSearchingNode] = ++occur;

                //we found a better pair?
                if (occur > currentMaxOccur)
                {
                    currentMaxOccur = occur;

                    //we are continuosly changing "currentSearchingNode" during the loop, perform a deep copy into the current best pair
                    RecursivePair.Copy(currentSearchingNode, currentMax);
                }

                //the second element now becomes the first of the pair that we are going to read
                node = nextNode;
            }

            //If could only find a pair occuring less than 3 (I think the original compressor did so), we have nothing to do. No suitable pair could be found.
            if (currentMaxOccur < MINIMUM_PAIR_OCCURRENCES) return null;

            //return the best pair we could find
            return currentMax;
        }

        private static void ModifyInput(LinkedList<RecursivePair> inputList, RecursivePair mostFrequent)
        {

            //currently accessed pair of the input data
            RecursivePair pair = new RecursivePair();

            LinkedListNode<RecursivePair> node = inputList.First;
            while (node != null && node.Next != null)
            {
                //read the current pair
                pair.First = node.Value;

                LinkedListNode<RecursivePair> nextNode = node.Next;
                pair.Second = nextNode.Value;

                //if this pair the most occurring one
                if (mostFrequent.Equals(pair))
                {
                    //replace the first part of the pair with the most occurring
                    node.Value = mostFrequent;

                    //remove he second part
                    inputList.Remove(nextNode);
                }

                node = node.Next;
            }
        }
    }

    #region "Internal classes"

    //This class represents either a simple byte or a recursive pair
    internal class RecursivePair : IEquatable<RecursivePair>
    {

        //Construct a new pair from an existing one
        public RecursivePair(RecursivePair n)
        {
            First = n.First;
            Second = n.Second;
            Value = n.Value;
        }

        //construct a "leaf" pair
        public RecursivePair(byte value = 0)
        {
            First = Second = null;
            Value = value;
        }

        //first part and second part of the pair
        public RecursivePair First { get; set; }
        public RecursivePair Second { get; set; }

        //this value only makes sense when this object represents a simple byte code
        public byte Value { get; set; }

        //this object is a simple byte code
        public bool IsLeaf { get { return First == null && Second == null; } }

        //used to "deep" copy a node into an already instantiated one (we don't really need to recursively copy also the two child pairs)
        public static void Copy(RecursivePair from, RecursivePair to)
        {
            to.First = from.First;
            to.Second = from.Second;
            to.Value = from.Value;
        }

        //euqality between pairs, in principle, they are really equal when they represents the very same sequence of bytecodes
        public bool Equals(RecursivePair node)
        {
            if (node == null) return false;

            if (IsLeaf && node.IsLeaf) return Value == node.Value;

            return ToString().Equals(node.ToString());
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RecursivePair);
        }

        //we just construct the HEX representation of this pair
        public override string ToString()
        {
            if (IsLeaf) return string.Format("{0:X2}", Value);
            return (First == null ? "" : First.ToString()) + (Second == null ? "" : Second.ToString());
        }

        //needed for storing pairs as key of a dictionary
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }

    #endregion
}

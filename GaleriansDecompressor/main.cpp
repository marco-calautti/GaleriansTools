/*
* This program decompresses the ".EXS" files from extracted from "SCRIPT.R2",
* ".Z2" files extracted from "BG.R2", "EVENT.R2" and "TAIMEN.R2".
*
* Please note that it cannot decode "SYSTEM.Z2" and ".Z2" files extracted
* from "SE.R2". Use "z2extract" for that.
*
* As for the algorithm, it is ripped right out of the game; as such, it's
* incomprehensible, but in a nutshell it's a simple dictionary compression
* algorithm.
*
* Since it is pulled directly from the game, no ownership to this code
* will be claim. It is probably copyrighted by Konami / KCE Tokyo or
* the team who develop it (Tenky - http://tenky.co.jp/katei.htm).
*
* This program is non-portable and will only work on MIPS, x86, and other
* little-endian architectures with x86-compatable C variable sizes.
*
* The different sgdecode-sxx varieties are to skip the xx byte header
* and start processing from there (start on the magic word "TEN2").
*
* This script is originally done by David Holmes (holme215@umn.edu) and
* modified by Rufas.
*
* Suikogaiden Translation Project:
* http://www.freewebs.com/ramsus-kun/Suikogaiden/Home.htm
*
*/

#include <stdio.h> 
#include <stdlib.h>

unsigned char buffer[0x400];

int main(int argc, char** argv)
{
	unsigned char unknown1[5];
	unsigned char unknown2[6];
	unsigned char* inputBuffer;
	unsigned char* outputBuffer;
	unsigned char* bufferFilePtr;

	unsigned int inputBufferLen;
	unsigned int outputBufferLen;

	int i;
	int bytesRead;

	FILE* inputFile;
	FILE* outputFile;

	if (argc != 3) {
		printf("Galerians (PSX) File Decompressor (C) Suikogaiden Translation Project\nCorrections and adaption of code: Phoenix - SadNES cITy Translations\n");
		printf("Usage: %s inputfile outputfile\n", argv[0]);
		return -1;
	}

	inputFile = fopen(argv[1], "rb");
	if (inputFile == NULL) {
		printf("Could not open %s!\n", argv[1]);
		perror("Reason");
		return -1;
	}


	// Speculation: file size minus header
	bytesRead = fread(unknown1, 1, 4, inputFile);
	if (bytesRead != 4) {
		printf("Error reading from file! %d bytes read!\n", bytesRead);
		perror("Reason");
		return -1;
	}

	unknown1[4] = 0;

	inputBufferLen = *(int*)unknown1;

	outputBufferLen = 1024 * 1024 * 1024; //\MB decompression buffer (just to be sure)
	outputBuffer = (unsigned char*)malloc(outputBufferLen);
	inputBuffer = (unsigned char*)malloc(inputBufferLen + 1); //there is a bug in the code, this is a dirty fix I know

	// The entire remainder of the file
	bytesRead = fread(inputBuffer, 1, inputBufferLen, inputFile);
	if (bytesRead != inputBufferLen) { //TODO: Fix numbers
		printf("Error reading from file! %d bytes read, %d bytes expected\n", bytesRead, inputBufferLen);
		return -1;
	}

	/* MIPS R3000a Registers */
	int zr;
	int v0;
	int v0temp;
	int v1;
	unsigned char* v1p; // Assembly reuses this register for both int and pointer types
	unsigned char* a0;
	unsigned char* a1;
	unsigned char* a2;
	int a3;
	unsigned char* a3p; // Assembly reuses this register for both int and pointer types
	int t0;
	unsigned char* t1;
	int t2;
	int t3;
	unsigned char* t4;
	int t5;
	int t6;
	int t7 = 0x100;

	/* These seem to be initialized at the beginning of the algorithm */
	zr = 0;
	a0 = inputBuffer;
	a1 = outputBuffer;
	a2 = inputBuffer + bytesRead; //TODO: This is a guess


	/*
	* This first part creates a "default" dictionary, containing no actual keys
	* (each key looks up itself)/
	*/

	t2 = *(a0)& 0xff;
	a3 = t2 & 0xff;
	a0++;
	t4 = buffer; // This is 0x1f800000 in game; we allocated "buffer" to be that space
X51e64:
	t2 = *(a0)& 0xff;
	a0++;
	v1 = 0;
	for (int i = 0; i<0x100; i++)
		buffer[i + 0x200] = i;



	/*
	* This second part reads the dictionary from the input file and stores it in
	* a pair 0x100-byte arrays, at buffer + 0x200 and buffer + 0x300 respectively. In
	* the game, "buffer" is a global located at 0x1f800000.
	* I suppose that the M-Type index is used whenever the code to be mapped to some pair is "close" to the current buffer w.r.t. some threshold, and then
	* we can use dummy pairs (codes mapping to the dictionar cursor) to do some shift and get to the desired position in the dictionary.
	* This way you don't need a new index byte for every code you want to store in the dictionary.
	*/

X51e8c:    v0 = a3 < 0x80; //is the index byte a pair coding?
	v0temp = v0;

	v0 = v1 + 0xffffff81; //this is just needed to take into account the 0x80 flag, we could remove this flag and add the index directly to v1 plus one

	if (v0temp != zr) goto X51ed8; //if the index byte is an M type, jump
X51e98:    v1 = v0 + a3; //update the dictionary cursor with the byte index

	a3p = v1 + t4; //compute the base offset in the dictionary

	if (v1 == t7) goto X51f30; //if the cursor on the dictionary goes to 0x100, the dictionary to be read is ended
X51ea4:    v0 = t2 & 0xff; //first byte of the pair

	*(a3p + 0x200) = (unsigned char)t2; //store the first byte of the pair mapped to the index
	//printf("%02X -> %02X ",v1,t2);
	if (v0 == v1){
		//printf("\n");
		goto X51ec8; //if the index maps to itself (i.e. to the first byte), skip
	}
X51eb0:    v0 = (*a0) & 0xff; //otherwise read the second byte of the pair

	*(a3p + 0x300) = (unsigned char)v0; //and store it as well
	//printf("%02X\n",v0);
X51ebc:    t2 = *(a0 + 1) & 0xff; //read the next index byte 

	a0 += 2;

	goto X51ed0;
X51ec8:    t2 = *(a0)& 0xff;

	a0++;
X51ed0:    v1++; goto X51f1c; //every index is a delta with refer to the previous index, so we need to store the previous index (we also increment it)

	//M-type index
X51ed8: t0 = a3 + zr; //place the index byte into t0 (t0 is a counter of pairs) (the counter is -1)

	if (a3 < 0) goto X51f1c; // I don't think this should ever actually happen (I agree)
X51ee0:    a3p = v1 + t4; //here we use the current dictionary cursor, and start placing pairs incrementally

	v0 = t2 & 0xff; //first byte of the current pair

	*(a3p + 0x200) = (unsigned char)t2; //put the first byte of the pair for the current dictionary cursor
	//printf("%02X -> %02X ",v1,t2);
	if (v0 == v1){
		//printf("\n");
		goto X51f08; //again, if the cursor maps to iteself, we don't have a second byte and we skip to the next pair/next index
	}
X51ef0:    v0 = (*a0) & 0xff; //otherwise, read the second byte of the pair

	*(a3p + 0x300) = (unsigned char)v0; //and write it in the dictionary
	//printf("%02X\n",v0);

	t2 = *(a0 + 1) & 0xff; //first byte of the next pair

	a0 += 2;

	goto X51f10; //go decrement the counter of pairs and increment the dictionary cursor
X51f08:    t2 = *(a0)& 0xff; //read the next index/next pair first byte

	a0++; //skip to the next pair
X51f10:    t0--; //decrement the pair counter

	v1++; //move the dictionary cursor

	if (t0 >= 0) goto X51ee0; //if we have still some pairs for the current M-Type index, continue 
X51f1c: a3 = t2; //place the next index byte into a3

	if (v1 == t7) goto X51f30; //we moved the dictionary cursor forward, is it 0x100? If yes, we end with the dictionary
X51f24:    t2 = *(a0)& 0xff; //first byte of the next possible pair
X51f28: a0++; goto X51e8c; //come back to check the index byte

	/*
	* This third section does the actual decompression, reading bytes, looking
	* them up in the dictionary, and writing out the expanded values.    It
	* eventually returns back up to the first section and rewrites the dictionary.
	*/


X51f30:
	//printf("\n");
	v1 = t2 << 0x8; //shift first byte to left
	t2 = *(a0 + 1) & 0xff; //third byte
	v0 = *(a0)& 0xff; //second byte
	a0 += 2;
	t3 = v1 + v0; //make a short with the first two bytes, it is a counter of bytes of the compressed segment
	a3p = t4 + zr; //base dictionary position
	if (t3 == zr) goto X51fb0; //is the counter equal zero?, if yes, prepare to read another dictionary
	t3--; //decrease the counter
X51f50:    v1 = t2 & 0xff; //current byte on compressed segment
	t2 = *(a0)& 0xff; //next byte on compressed segment
	a0++;
	goto X51f68; //go decompress with current byte
X51f60:    v1 = *(a3p - 1) & 0xff; //read the nex byte of the sequence to be expanded (they are in reverse order)
	a3p--;
X51f68:    t1 = v1 + t4; //use current byte to shift in dictionary
	t0 = *(t1 + 0x200) & 0xff; // Read L-byte from dictionary
	*(a1) = (unsigned char)v1; //output the read byte, if it maps to itself then it is uncompressed and it needs to be placed here, otherwise at each recursive expanding step it becomes the first byte of the recursively expanded sequence
	v1 = v1 & 0xff;
	v0 = t0 & 0xff;
	if (v1 == v0) goto X51f98; //if the byte read from dictionary is mapped to itself, then this byte is an uncompressed byte, skip the second byte part
	v0 = *(t1 + 0x300) & 0xff; // Read R-byte from dictionary
	*(a3p + 1) = (unsigned char)t0; //push in reverse order
	*(a3p) = (unsigned char)v0; //the read pair in order to continue expanding pairs
	a3p += 2;
	goto X51f9c;
X51f98:    a1++; //move forward into output
X51f9c:    if (a3p != t4) goto X51f60; //if we have still codes into the temporary buffer to be expanded/transferred to output, recursively continue
	if (t3-- != zr) goto X51f50; // if we have still more data to consume in this segment, continue
	t3++; // yeah, compilers are weird, I know (In fact this is not needed at all)
X51fb0:    v0 = a2 < a0; //check if there is some other dictionary to read
	a3 = t2 & 0xff;
	if (v0 == zr) goto X51e64; //this is needed for the multiple dictionary version of the compression algorithm

X51fbc:

	/* At this point the entire file should be decompressed into outputBuffer */

	outputFile = fopen(argv[2], "wb+");
	if (outputFile == NULL) {
		printf("Could not open %s!\n", argv[1]);
		perror("Reason");
		return -1;
	}

	fwrite(outputBuffer, 1, a1 - outputBuffer, outputFile);

	fclose(outputFile);
	free(inputBuffer);
	free(outputBuffer);

	return 0;
}